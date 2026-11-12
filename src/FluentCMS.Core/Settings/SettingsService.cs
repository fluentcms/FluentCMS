using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Transfer;
using FluentCMS.Events;
using FluentCMS.Settings;
using FluentCMS.Sites;
using FluentCMS.Transfer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Settings;

// The settings of every area, as documents. ISettings<T> is a typed face on it, and the Management endpoint (B-50) is another.
// A typed read is not checked against permissions; the redacted read and a write need settings.{area}.manage for the site, or with no site limit for the
// instance defaults. A write goes through a context of its own, as the outbox does, so that it saves nothing the caller has
// tracked and drops nothing the caller has tracked. It replaces the row with one statement that names the version it read, and
// starts again when another writer got there first, so two writers never see half of each other's document and the later one
// stands; a caller that names the version it read is refused instead when the row has moved on.
internal sealed class SettingsService(
    SystemDbContext context,
    IServiceScopeFactory scopes,
    SettingsRegistry registry,
    SettingsCache cache,
    IAuthorizationEvaluator authorization,
    IOperationContextAccessor accessor,
    IEventBus bus,
    ITransactionProbe transaction,
    TimeProvider time,
    ILogger<SettingsService> logger)
{
    // Every lost round means another writer won it, so a writer that loses this many in a row is among very many.
    private const int Attempts = 100;

    // For the Management API: the permission is asked by the area's name first, so a caller without it cannot tell which areas exist.
    public async Task<SettingsDescriptor> RequireManagedAsync(string area, SiteId? site, CancellationToken cancellationToken)
    {
        var operation = accessor.Current
            ?? throw new InvalidOperationException("Managing settings runs inside an operation; the entry point sets IOperationContextAccessor.Current.");

        var permissions = await authorization.ResolveAsync(operation, cancellationToken);
        return permissions.Allows(CorePermissions.Settings(area), new AccessTarget(site, null)) ? Require(area) : throw new SettingsAccessDeniedException(area, site);
    }

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

        var own = rows.FirstOrDefault(row => row.SiteId is not null);
        var instance = rows.FirstOrDefault(row => row.SiteId is null);

        // The version is that of the row a write to this scope replaces, also when that row is skipped as unreadable: the
        // caller must be able to name it, or it could never save over a row that cannot be read.
        var version = (site is null ? instance : own)?.Version ?? 0;
        var resolved = Pick(descriptor, own, SettingsSource.Site, version, scope)
            ?? Pick(descriptor, instance, SettingsSource.Default, version, SettingsCache.InstanceScope)
            ?? new ResolvedSetting(descriptor.Write(descriptor.CreateDefault()), SettingsSource.Code, version);

        cache.Set(scope, area, resolved, generation);
        return resolved;
    }

    // What the Management endpoint shows: the resolved document without its secret keys, and the secret keys that hold a value in
    // it. The version is that of the row a write to this site (or to the instance defaults) would replace, so that it can go
    // back as the version a write names: 0 when that row does not exist, even if the document came from a level below it, and
    // the version of the row that is there even if it was skipped because the class cannot read it.
    // It is what an admin edits, so it needs the permission a write needs.
    public async Task<RedactedSetting> ResolveRedactedAsync(SettingsDescriptor descriptor, SiteId? site, CancellationToken cancellationToken)
    {
        await RequirePermissionAsync(descriptor, site, cancellationToken);
        var resolved = await ResolveAsync(descriptor, site, cancellationToken);
        var (json, secretsSet) = descriptor.Redact(resolved.Json);
        return new RedactedSetting(json, resolved.Source, resolved.Version, resolved.Source == SettingsSource.Code ? [] : secretsSet);
    }

    // Replaces a document. A null site is the instance defaults. The version is the one the caller read (0 for no row), or
    // null to replace whatever is there. A secret key that the document leaves out keeps the stored value: that of the row it replaces, or for a site with none, of the instance default.
    public async Task WriteAsync(SettingsDescriptor descriptor, SiteId? site, string json, int? expectedVersion, CancellationToken cancellationToken)
    {
        var operation = await AuthorizeAsync(descriptor, site, cancellationToken);

        // Refused before the database is touched; the document that is stored is built again from the row it replaces.
        descriptor.Canonicalize(json);

        await StoreAsync(descriptor, site, json, expectedVersion, operation.AuditPrincipal.Id, cancellationToken);
        await ChangedAsync(descriptor, site, operation, "changed", cancellationToken);
    }

    // Removes a document, so that the level below shows through again. Nothing was there, nothing is raised.
    public async Task ResetAsync(SettingsDescriptor descriptor, SiteId? site, int? expectedVersion, CancellationToken cancellationToken)
    {
        var operation = await AuthorizeAsync(descriptor, site, cancellationToken);

        if (!await RemoveAsync(descriptor, site, expectedVersion, cancellationToken))
        {
            return;
        }

        await ChangedAsync(descriptor, site, operation, "reset", cancellationToken);
    }

    // For an export, which has checked transfer.manage: the site's own documents, without their secrets. One that its class
    // cannot read, or whose area no longer exists, is left out, as a read would skip it.
    public async Task<IReadOnlyList<(string Area, string Json)>> SiteDocumentsAsync(SiteId site, CancellationToken cancellationToken)
    {
        Guid? siteId = site.Value;
        var rows = await context.Set<SettingRow>().AsNoTracking()
            .Where(row => row.SiteId == siteId)
            .ToListAsync(cancellationToken);

        List<(string Area, string Json)> documents = [];
        foreach (var row in rows.OrderBy(row => row.Key, StringComparer.Ordinal))
        {
            if (registry.Find(row.Key) is { } descriptor && descriptor.CanRead(row.Json))
            {
                documents.Add((row.Key, descriptor.Redact(row.Json).Json));
            }
        }

        return documents;
    }

    // For an import: a document without secrets, which keep what the site has, as a write that leaves them out does. The
    // same document as the site's is no write. A dry run checks the document and writes nothing. The caller who started the
    // import needs settings.{area}.manage in the site, as a write of its own would; the write itself is the import's.
    public async Task<TransferOutcome> ImportAsync(string area, SiteId site, string json, bool dryRun, OperationContext caller, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);

        var descriptor = registry.Find(area)
            ?? throw SettingsDescriptor.Fail("area", TransferErrorCodes.UnknownArea, $"There is no settings area '{area}' in this instance.");
        if (!(await authorization.ResolveAsync(caller, cancellationToken)).Allows(descriptor.Permission, new AccessTarget(site, null)))
        {
            throw new SettingsAccessDeniedException(descriptor.Area, site);
        }

        var canonical = descriptor.Canonicalize(json);

        Guid? siteId = site.Value;
        var stored = await context.Set<SettingRow>().AsNoTracking()
            .Where(row => row.Key == area && row.SiteId == siteId)
            .Select(row => row.Json)
            .FirstOrDefaultAsync(cancellationToken);
        if (stored is not null
            && descriptor.CanRead(stored)
            && JsonNode.DeepEquals(JsonNode.Parse(descriptor.Redact(stored).Json), JsonNode.Parse(descriptor.Redact(canonical).Json)))
        {
            return TransferOutcome.Skipped;
        }

        if (!dryRun)
        {
            await WriteAsync(descriptor, site, json, expectedVersion: null, cancellationToken);
        }

        return stored is null ? TransferOutcome.Created : TransferOutcome.Updated;
    }

    private ResolvedSetting? Pick(SettingsDescriptor descriptor, SettingRow? row, SettingsSource source, int version, Guid scope)
    {
        if (row is null)
        {
            return null;
        }

        if (descriptor.CanRead(row.Json))
        {
            return new ResolvedSetting(row.Json, source, version);
        }

        SettingsLog.Unreadable(logger, descriptor.Area, ScopeName(scope));
        return null;
    }

    private async Task<OperationContext> RequirePermissionAsync(SettingsDescriptor descriptor, SiteId? site, CancellationToken cancellationToken)
    {
        var operation = accessor.Current
            ?? throw new InvalidOperationException("Managing settings runs inside an operation; the entry point sets IOperationContextAccessor.Current.");

        var permissions = await authorization.ResolveAsync(operation, cancellationToken);
        if (!permissions.Allows(descriptor.Permission, new AccessTarget(site, null)))
        {
            throw new SettingsAccessDeniedException(descriptor.Area, site);
        }

        return operation;
    }

    private async Task<OperationContext> AuthorizeAsync(SettingsDescriptor descriptor, SiteId? site, CancellationToken cancellationToken)
    {
        var operation = await RequirePermissionAsync(descriptor, site, cancellationToken);

        // The event is raised once the write has committed. Inside a transaction of the caller it has not, and may not.
        if (transaction.InTransaction)
        {
            throw new InvalidOperationException(
                $"{SettingsErrorCodes.WriteInTransaction}: settings cannot be changed inside a transaction, because the event that follows must not be raised for a write that may still roll back; change them after the transaction.");
        }

        return operation;
    }

    private static async Task EnsureSiteAsync(SystemDbContext db, Guid? site, CancellationToken cancellationToken)
    {
        if (site is { } one && (one == Guid.Empty || !await db.Set<Site>().AnyAsync(item => item.Id == one, cancellationToken)))
        {
            throw SiteNotFound(new SiteId(one));
        }
    }

    private async Task StoreAsync(SettingsDescriptor descriptor, SiteId? siteId, string json, int? expectedVersion, Guid actor, CancellationToken cancellationToken)
    {
        var site = siteId?.Value;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        await EnsureSiteAsync(db, site, cancellationToken);

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var current = await Rows(db, descriptor.Area, site).AsNoTracking()
                .Select(row => new { row.Json, row.Version })
                .FirstOrDefaultAsync(cancellationToken);

            var version = current?.Version ?? 0;
            if (expectedVersion is { } expected && expected != version)
            {
                throw Conflict(descriptor);
            }

            // The secrets that the document leaves out are the ones the caller was shown, which are those of the document the read
            // resolved to. A row that the class cannot read was skipped by the read, so its secrets were not shown and are not used:
            // a site without a readable row of its own takes them from the instance default if that can be read, and from the code.
            var stored = current is not null && descriptor.CanRead(current.Json) ? current.Json : null;
            var skipped = current is not null && stored is null ? descriptor.SecretsLeftOut(json, current.Json) : [];
            if (stored is null && site is not null)
            {
                var inherited = await Rows(db, descriptor.Area, null).AsNoTracking().Select(row => row.Json).FirstOrDefaultAsync(cancellationToken);
                stored = inherited is not null && descriptor.CanRead(inherited) ? inherited : null;
            }

            var canonical = descriptor.Canonicalize(descriptor.KeepSecrets(json, stored, out var dropped));
            foreach (var key in dropped.Concat(skipped))
            {
                SettingsLog.SecretDropped(logger, descriptor.Area, key, ScopeName(site ?? SettingsCache.InstanceScope));
            }

            var now = time.GetUtcNow();
            if (current is not null)
            {
                var updated = await Rows(db, descriptor.Area, site).Where(row => row.Version == version).ExecuteUpdateAsync(
                    set => set
                        .SetProperty(row => row.Json, canonical)
                        .SetProperty(row => row.Version, version + 1)
                        .SetProperty(row => row.UpdatedAt, now)
                        .SetProperty(row => row.UpdatedBy, actor),
                    cancellationToken);

                if (updated > 0)
                {
                    return;
                }
            }
            else
            {
                db.Add(new SettingRow { Id = Guid.CreateVersion7(), SiteId = site, Key = descriptor.Area, Json = canonical, Version = 1, UpdatedAt = now, UpdatedBy = actor });
                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                    return;
                }
                catch (DbUpdateException)
                {
                    db.ChangeTracker.Clear();
                    await EnsureSiteAsync(db, site, cancellationToken);

                    // Another writer inserted the row first, which the next round sees; anything else is a real failure.
                    if (!await Rows(db, descriptor.Area, site).AnyAsync(cancellationToken))
                    {
                        throw;
                    }
                }
            }

            // The row moved between the read and the write. A caller that named a version has lost; any other starts again.
            if (expectedVersion is not null)
            {
                throw Conflict(descriptor);
            }
        }

        throw Conflict(descriptor);
    }

    // Whether there was a document to remove. Version 0 says there was none, so a row that is there is a conflict.
    private async Task<bool> RemoveAsync(SettingsDescriptor descriptor, SiteId? siteId, int? expectedVersion, CancellationToken cancellationToken)
    {
        var site = siteId?.Value;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        await EnsureSiteAsync(db, site, cancellationToken);

        var rows = Rows(db, descriptor.Area, site);
        if (expectedVersion == 0)
        {
            return await rows.AnyAsync(cancellationToken) ? throw Conflict(descriptor) : false;
        }

        if (expectedVersion is { } version)
        {
            rows = rows.Where(row => row.Version == version);
        }

        if (await rows.ExecuteDeleteAsync(cancellationToken) > 0)
        {
            return true;
        }

        return expectedVersion is null ? false : throw Conflict(descriptor);
    }

    private static IQueryable<SettingRow> Rows(SystemDbContext db, string area, Guid? site)
    {
        var rows = db.Set<SettingRow>().Where(row => row.Key == area);
        return site is { } one ? rows.Where(row => row.SiteId == one) : rows.Where(row => row.SiteId == null);
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
