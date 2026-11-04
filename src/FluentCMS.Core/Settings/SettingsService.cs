using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using FluentCMS.Settings;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Settings;

// The settings of every area, as documents. ISettings<T> is a typed face on it, and the Management endpoint (B-50) is another.
// A read is not checked against permissions; a write needs settings.{area}.manage for the site, or with no site limit for the
// instance defaults. A write is one statement on one row, so two writers never see half of each other's document; the later one
// stands, unless the caller names the version it read, in which case a row that has moved on is refused.
internal sealed class SettingsService(
    SystemDbContext context,
    SettingsRegistry registry,
    SettingsCache cache,
    IAuthorizationEvaluator authorization,
    IOperationContextAccessor accessor,
    IEventBus bus,
    ITransactionProbe transaction,
    TimeProvider time,
    ILogger<SettingsService> logger)
{
    private const int InsertRetries = 3;

    public SettingsDescriptor Require(string area) =>
        registry.Find(area) ?? throw SettingsDescriptor.Fail("area", SettingsErrorCodes.UnknownArea, $"There is no settings area '{area}'.");

    // Site override, then instance default, then code default. A null site is the instance defaults. The document that is
    // returned is one the class can read, so a damaged row is skipped and never takes a site down.
    public async Task<ResolvedSetting> ResolveAsync(SettingsDescriptor descriptor, SiteId? site, CancellationToken cancellationToken)
    {
        var scope = site?.Value ?? SettingsCache.InstanceScope;
        if (site is { IsEmpty: true })
        {
            throw SiteNotFound(site.Value);
        }

        if (cache.TryGet(scope, descriptor.Area, out var cached))
        {
            return cached;
        }

        var generation = cache.Generation(descriptor.Area);
        var area = descriptor.Area;
        Guid? siteId = site?.Value;
        var rows = await context.Set<SettingRow>().AsNoTracking()
            .Where(row => row.Key == area && (row.SiteId == null || row.SiteId == siteId))
            .ToListAsync(cancellationToken);

        if (site is not null && rows.All(row => row.SiteId is null) && !await context.Set<Site>().AnyAsync(item => item.Id == scope, cancellationToken))
        {
            throw SiteNotFound(site.Value);
        }

        var resolved = Pick(descriptor, rows.FirstOrDefault(row => row.SiteId is not null), SettingsSource.Site, scope)
            ?? Pick(descriptor, rows.FirstOrDefault(row => row.SiteId is null), SettingsSource.Default, SettingsCache.InstanceScope)
            ?? new ResolvedSetting(descriptor.Write(descriptor.CreateDefault()), SettingsSource.Code, 0);

        cache.Set(scope, area, resolved, generation);
        return resolved;
    }

    // Replaces a document. A null site is the instance defaults. The version is the one the caller read (0 for no row), or
    // null to replace whatever is there.
    public async Task WriteAsync(SettingsDescriptor descriptor, SiteId? site, string json, int? expectedVersion, CancellationToken cancellationToken)
    {
        var operation = await AuthorizeAsync(descriptor, site, cancellationToken);
        var canonical = descriptor.Canonicalize(json);
        await EnsureSiteAsync(site, cancellationToken);

        var actor = operation.AuditPrincipal.Id;
        await StoreAsync(descriptor, site?.Value, canonical, expectedVersion, actor, cancellationToken);
        await ChangedAsync(descriptor, site, operation, "changed", cancellationToken);
    }

    // Removes a document, so that the level below shows through again. Nothing was there, nothing is raised.
    public async Task ResetAsync(SettingsDescriptor descriptor, SiteId? site, int? expectedVersion, CancellationToken cancellationToken)
    {
        var operation = await AuthorizeAsync(descriptor, site, cancellationToken);
        await EnsureSiteAsync(site, cancellationToken);

        var removed = await Rows(descriptor.Area, site?.Value, expectedVersion).ExecuteDeleteAsync(cancellationToken);
        if (removed == 0)
        {
            if (expectedVersion is > 0)
            {
                throw Conflict(descriptor);
            }

            return;
        }

        await ChangedAsync(descriptor, site, operation, "reset", cancellationToken);
    }

    private ResolvedSetting? Pick(SettingsDescriptor descriptor, SettingRow? row, SettingsSource source, Guid scope)
    {
        if (row is null)
        {
            return null;
        }

        if (descriptor.CanRead(row.Json))
        {
            return new ResolvedSetting(row.Json, source, row.Version);
        }

        SettingsLog.Unreadable(logger, descriptor.Area, ScopeName(scope));
        return null;
    }

    private async Task<OperationContext> AuthorizeAsync(SettingsDescriptor descriptor, SiteId? site, CancellationToken cancellationToken)
    {
        var operation = accessor.Current
            ?? throw new InvalidOperationException("Changing settings runs inside an operation; the entry point sets IOperationContextAccessor.Current.");

        var permissions = await authorization.ResolveAsync(operation, cancellationToken);
        if (!permissions.Allows(descriptor.Permission, new AccessTarget(site, null)))
        {
            throw new SettingsAccessDeniedException(descriptor.Area, site);
        }

        // The event is raised once the write has committed. Inside a transaction of the caller it has not, and may not.
        if (transaction.InTransaction)
        {
            throw new InvalidOperationException(
                $"{SettingsErrorCodes.WriteInTransaction}: settings cannot be changed inside a transaction, because the event that follows must not be raised for a write that may still roll back; change them after the transaction.");
        }

        return operation;
    }

    private async Task EnsureSiteAsync(SiteId? site, CancellationToken cancellationToken)
    {
        if (site is { } one && (one.IsEmpty || !await context.Set<Site>().AnyAsync(item => item.Id == one.Value, cancellationToken)))
        {
            throw SiteNotFound(one);
        }
    }

    private async Task StoreAsync(SettingsDescriptor descriptor, Guid? site, string json, int? expectedVersion, Guid actor, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < InsertRetries; attempt++)
        {
            var now = time.GetUtcNow();

            // Version 0 says there was no row, so the only thing left to try is the insert.
            if (expectedVersion != 0)
            {
                var updated = await Rows(descriptor.Area, site, expectedVersion).ExecuteUpdateAsync(
                    set => set
                        .SetProperty(row => row.Json, json)
                        .SetProperty(row => row.Version, row => row.Version + 1)
                        .SetProperty(row => row.UpdatedAt, now)
                        .SetProperty(row => row.UpdatedBy, actor),
                    cancellationToken);

                if (updated > 0)
                {
                    return;
                }

                if (expectedVersion is > 0)
                {
                    throw Conflict(descriptor);
                }
            }

            context.Add(new SettingRow { Id = Guid.CreateVersion7(), SiteId = site, Key = descriptor.Area, Json = json, Version = 1, UpdatedAt = now, UpdatedBy = actor });
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException)
            {
                context.ChangeTracker.Clear();
                if (site is { } one && !await context.Set<Site>().AnyAsync(item => item.Id == one, cancellationToken))
                {
                    throw SiteNotFound(new SiteId(one));
                }

                // Another writer inserted the row first. A caller that named no version just replaces it, and one that named 0 lost.
                if (!await Rows(descriptor.Area, site, null).AnyAsync(cancellationToken))
                {
                    throw;
                }

                if (expectedVersion is 0)
                {
                    throw Conflict(descriptor);
                }
            }
        }

        throw Conflict(descriptor);
    }

    private IQueryable<SettingRow> Rows(string area, Guid? site, int? expectedVersion)
    {
        var rows = context.Set<SettingRow>().Where(row => row.Key == area);
        rows = site is { } one ? rows.Where(row => row.SiteId == one) : rows.Where(row => row.SiteId == null);
        return expectedVersion is { } version and > 0 ? rows.Where(row => row.Version == version) : rows;
    }

    private async Task ChangedAsync(SettingsDescriptor descriptor, SiteId? site, OperationContext operation, string change, CancellationToken cancellationToken)
    {
        cache.Invalidate(descriptor.Area);
        var scope = ScopeName(site?.Value ?? SettingsCache.InstanceScope);
        SettingsLog.Changed(logger, descriptor.Area, scope, change, operation.AuditPrincipal.Id);

        var source = EventSource.From(operation) with { SiteId = site?.Value ?? Guid.Empty };
        await bus.PublishAsync(new SettingsChangedEvent(descriptor.Area) { Source = source }, cancellationToken);
    }

    private static string ScopeName(Guid scope) => scope == SettingsCache.InstanceScope ? "the instance" : $"site {scope}";

    private static ValidationException SiteNotFound(SiteId site) =>
        SettingsDescriptor.Fail("site", SettingsErrorCodes.SiteNotFound, $"There is no site {site}.");

    private static ValidationException Conflict(SettingsDescriptor descriptor) =>
        SettingsDescriptor.Fail("", SettingsErrorCodes.VersionConflict, $"The settings area '{descriptor.Area}' changed since it was read; read it again and retry.");
}
