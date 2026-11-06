using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCMS.Activity;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Localization;
using FluentCMS.Core.Settings;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Sites;

internal sealed record SiteInfo(Guid Id, string Name, string Slug, string DefaultLocale, bool IsDefault, DateTimeOffset CreatedAt);

internal sealed record SiteLocaleInfo(string Code, int Position, IReadOnlyList<string>? Fallbacks, bool Enabled);

// A null member leaves the value as it is.
internal sealed record SiteChange
{
    public string? Name { get; init; }

    public string? DefaultLocale { get; init; }
}

// A null member leaves the value as it is, except that SetsFallbacks with null Fallbacks goes back to the implicit chain.
internal sealed record SiteLocaleChange
{
    public int? Position { get; init; }

    public bool? Enabled { get; init; }

    public bool SetsFallbacks { get; init; }

    public IReadOnlyList<string>? Fallbacks { get; init; }
}

// What creating and deleting a site does to the rows around it, and the site and locale changes of the Management API.
// Those check sites.manage for the site; creating, deleting and EnsureLocaleAsync are the host's and the seeder's.
internal sealed partial class SiteManager(
    CmsDbContext context,
    TimeProvider time,
    SettingsCache settings,
    IActivityLog activity,
    ManagementAuthority authority,
    IEventBus bus,
    IOperationContextAccessor accessor)
{
    private const int SlugLength = 64;
    private const int NameLength = 128;

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*\\z", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();

    // The site, its default locale and its Public role go in one save, so a site never exists without its Public role.
    public async Task<Site> CreateAsync(string name, string slug, string defaultLocale, CancellationToken cancellationToken = default)
    {
        List<ValidationError> errors = [];
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add(new("name", SiteErrorCodes.InvalidSite, "A site needs a name."));
        }

        if (slug.Length > SlugLength || !SlugPattern().IsMatch(slug))
        {
            errors.Add(new("slug", SiteErrorCodes.InvalidSite, $"A slug is lower case letters and digits joined by hyphens, up to {SlugLength} characters."));
        }
        else if (await context.Set<Site>().AnyAsync(site => site.Slug == slug, cancellationToken))
        {
            errors.Add(new("slug", SiteErrorCodes.InvalidSite, $"A site with the slug '{slug}' exists."));
        }

        if (!LocaleTag.TryNormalize(defaultLocale, out var locale))
        {
            errors.Add(new("defaultLocale", LocaleErrorCodes.InvalidLocale, $"'{defaultLocale}' is not a well-formed BCP 47 language tag."));
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var site = new Site
        {
            Id = Guid.CreateVersion7(),
            Name = name.Trim(),
            Slug = slug,
            DefaultLocale = locale,
            IsDefault = false,
            CreatedAt = time.GetUtcNow(),
        };

        context.Add(site);
        context.Add(new SiteLocale { SiteId = site.Id, Code = locale, Position = 0, Enabled = true });
        context.Add(RoleSeed.PublicOf(Guid.CreateVersion7(), site.Id));
        await context.SaveChangesAsync(cancellationToken);
        return site;
    }

    // Adds a locale to a site's allow-list if it has none by that code, after the others. A locale that exists is left as it is, enabled or not.
    public async Task EnsureLocaleAsync(Guid siteId, string code, CancellationToken cancellationToken = default)
    {
        if (!LocaleTag.TryNormalize(code, out var locale))
        {
            throw new ValidationException([new ValidationError("locale", LocaleErrorCodes.InvalidLocale, $"'{code}' is not a well-formed BCP 47 language tag.")]);
        }

        var rows = await context.Set<SiteLocale>().AsNoTracking().Where(item => item.SiteId == siteId).Select(item => new { item.Code, item.Position }).ToListAsync(cancellationToken);
        if (rows.Any(item => string.Equals(item.Code, locale, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var row = new SiteLocale { SiteId = siteId, Code = locale, Position = rows.Count == 0 ? 0 : rows.Max(item => item.Position) + 1, Enabled = true };
        context.Add(row);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another instance added it between the read and the insert; that is what was wanted.
            context.Entry(row).State = EntityState.Detached;
            if (await context.Set<SiteLocale>().AsNoTracking().AnyAsync(item => item.SiteId == siteId && item.Code == locale, cancellationToken))
            {
                return;
            }

            throw;
        }

        await activity.WriteAsync(new ActivityEntry("site.locale-added") { TargetType = "site", TargetId = siteId.ToString("D"), Locale = locale }, cancellationToken);
    }

    // The foreign keys delete the site's locales, its roles with their grants, and its settings. A site with documents stays, because
    // they would be left in a site that no longer exists; trashed ones count, since restoring one needs its site.
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var site = await context.Set<Site>().AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new ValidationException([new ValidationError("site", SiteErrorCodes.SiteNotFound, $"There is no site {id}.")]);

        if (site.IsDefault)
        {
            throw new ValidationException([new ValidationError("site", SiteErrorCodes.DefaultSiteProtected, "The default site cannot be deleted.")]);
        }

        if (await context.Documents.IgnoreQueryFilters().AnyAsync(document => document.SiteId == id, cancellationToken))
        {
            throw new ValidationException([new ValidationError("site", SiteErrorCodes.SiteHasContent, $"The site '{site.Slug}' still has content. Delete it first.")]);
        }

        await context.Set<Site>().Where(item => item.Id == id).ExecuteDeleteAsync(cancellationToken);
        settings.InvalidateSite(id);
    }

    // The sites the caller manages, the default first.
    public async Task<IReadOnlyList<SiteInfo>> ListAsync(CancellationToken cancellationToken)
    {
        var sites = await context.Set<Site>().AsNoTracking().ToListAsync(cancellationToken);

        List<SiteInfo> managed = [];
        foreach (var site in sites.OrderByDescending(item => item.IsDefault).ThenBy(item => item.Slug, StringComparer.Ordinal))
        {
            if (await authority.AllowsAsync(CorePermissions.SitesManage, site.Id, cancellationToken))
            {
                managed.Add(Info(site));
            }
        }

        if (managed.Count == 0)
        {
            await authority.RequireAsync(CorePermissions.SitesManage, accessor.Current?.Site.Value, cancellationToken);
        }

        return managed;
    }

    public async Task<SiteInfo> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await authority.RequireAsync(CorePermissions.SitesManage, id, cancellationToken);
        return Info(await FindAsync(id, tracked: false, cancellationToken));
    }

    // The default locale is one the site allows and has enabled.
    public async Task<SiteInfo> UpdateAsync(Guid id, SiteChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        await authority.RequireAsync(CorePermissions.SitesManage, id, cancellationToken);
        var site = await FindAsync(id, tracked: true, cancellationToken);

        List<ValidationError> errors = [];
        if (change.Name is { } name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > NameLength)
            {
                errors.Add(new("name", SiteErrorCodes.InvalidSite, $"A site has a name of 1 to {NameLength} characters."));
            }
            else
            {
                site.Name = name.Trim();
            }
        }

        if (change.DefaultLocale is { } locale)
        {
            var rows = await LocalesOfAsync(id, cancellationToken);
            if (!LocaleTag.TryNormalize(locale, out var normalized))
            {
                errors.Add(new("defaultLocale", LocaleErrorCodes.InvalidLocale, $"'{locale}' is not a well-formed BCP 47 language tag."));
            }
            else if (rows.Find(row => row.Enabled && string.Equals(row.Code, normalized, StringComparison.OrdinalIgnoreCase)) is { } row)
            {
                site.DefaultLocale = row.Code;
            }
            else
            {
                errors.Add(new("defaultLocale", LocaleErrorCodes.LocaleNotAllowed, $"The default locale is one the site allows and has enabled, and '{normalized}' is not."));
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        if (context.Entry(site).State == EntityState.Modified)
        {
            await context.SaveChangesAsync(cancellationToken);
            await bus.PublishAsync(new SiteUpdatedEvent(site.Id, site.Slug) { Source = SourceOf(site.Id) }, cancellationToken);
        }

        return Info(site);
    }

    public async Task<IReadOnlyList<SiteLocaleInfo>> LocalesAsync(Guid id, CancellationToken cancellationToken)
    {
        await authority.RequireAsync(CorePermissions.SitesManage, id, cancellationToken);
        await FindAsync(id, tracked: false, cancellationToken);
        return [.. (await LocalesOfAsync(id, cancellationToken)).Select(Info)];
    }

    // After the others unless a position is given; a fallback names another locale of the site.
    public async Task<SiteLocaleInfo> AddLocaleAsync(Guid id, string code, int? position, bool enabled, IReadOnlyList<string>? fallbacks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);

        await authority.RequireAsync(CorePermissions.SitesManage, id, cancellationToken);
        var site = await FindAsync(id, tracked: false, cancellationToken);
        var rows = await LocalesOfAsync(id, cancellationToken);

        if (!LocaleTag.TryNormalize(code, out var locale))
        {
            throw new ValidationException([new ValidationError("code", LocaleErrorCodes.InvalidLocale, $"'{code}' is not a well-formed BCP 47 language tag.")]);
        }

        if (rows.Exists(row => string.Equals(row.Code, locale, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ValidationException([new ValidationError("code", LocaleErrorCodes.LocaleExists, $"The site has the locale '{locale}'.")]);
        }

        List<ValidationError> errors = [];
        var chain = Fallbacks(fallbacks, locale, rows, errors);
        if (position is < 0)
        {
            errors.Add(new("position", SiteErrorCodes.InvalidSite, "A position is zero or more."));
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var added = new SiteLocale
        {
            SiteId = id,
            Code = locale,
            Position = position ?? (rows.Count == 0 ? 0 : rows.Max(row => row.Position) + 1),
            Enabled = enabled,
            Fallbacks = chain is null ? null : JsonSerializer.Serialize(chain),
        };
        context.Add(added);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear();
            throw new ValidationException([new ValidationError("code", LocaleErrorCodes.LocaleExists, $"The site has the locale '{locale}'.")]);
        }

        await LogAsync("site.locale-added", site, locale, cancellationToken);
        return Info(added);
    }

    // The default locale stays enabled.
    public async Task<SiteLocaleInfo> UpdateLocaleAsync(Guid id, string code, SiteLocaleChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        await authority.RequireAsync(CorePermissions.SitesManage, id, cancellationToken);
        var site = await FindAsync(id, tracked: false, cancellationToken);
        var rows = await LocalesOfAsync(id, cancellationToken);
        var row = Locale(rows, code);

        List<ValidationError> errors = [];
        if (change.Enabled is false && IsDefault(site, row))
        {
            errors.Add(new("enabled", LocaleErrorCodes.LocaleInUse, $"'{row.Code}' is the default locale of the site, so it stays enabled."));
        }

        if (change.Position is < 0)
        {
            errors.Add(new("position", SiteErrorCodes.InvalidSite, "A position is zero or more."));
        }

        var chain = change.SetsFallbacks ? Fallbacks(change.Fallbacks, row.Code, rows, errors) : null;
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var tracked = context.Attach(row).Entity;
        tracked.Enabled = change.Enabled ?? tracked.Enabled;
        tracked.Position = change.Position ?? tracked.Position;
        if (change.SetsFallbacks)
        {
            tracked.Fallbacks = chain is null ? null : JsonSerializer.Serialize(chain);
        }

        await context.SaveChangesAsync(cancellationToken);
        await LogAsync("site.locale-updated", site, tracked.Code, cancellationToken);
        return Info(tracked);
    }

    // A locale with entries, trashed ones included, stays: removing it would leave them in a locale the site does not allow.
    // It can be disabled instead.
    public async Task RemoveLocaleAsync(Guid id, string code, CancellationToken cancellationToken)
    {
        await authority.RequireAsync(CorePermissions.SitesManage, id, cancellationToken);
        var site = await FindAsync(id, tracked: false, cancellationToken);
        var row = Locale(await LocalesOfAsync(id, cancellationToken), code);

        if (IsDefault(site, row))
        {
            throw new ValidationException([new ValidationError("code", LocaleErrorCodes.LocaleInUse, $"'{row.Code}' is the default locale of the site; make another the default first.")]);
        }

        var locale = row.Code;
        if (await context.Documents.IgnoreQueryFilters().AnyAsync(document => document.SiteId == id && document.Locale == locale, cancellationToken))
        {
            throw new ValidationException([new ValidationError("code", LocaleErrorCodes.LocaleInUse, $"The site has entries in '{locale}'. Disable the locale instead, or delete them first.")]);
        }

        await context.Set<SiteLocale>().Where(item => item.SiteId == id && item.Code == locale).ExecuteDeleteAsync(cancellationToken);
        await LogAsync("site.locale-removed", site, locale, cancellationToken);
    }

    private async Task<Site> FindAsync(Guid id, bool tracked, CancellationToken cancellationToken)
    {
        var sites = tracked ? context.Set<Site>() : context.Set<Site>().AsNoTracking();
        return await sites.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new ValidationException([new ValidationError("site", SiteErrorCodes.SiteNotFound, $"There is no site {id}.")]);
    }

    private async Task<List<SiteLocale>> LocalesOfAsync(Guid id, CancellationToken cancellationToken)
    {
        var rows = await context.Set<SiteLocale>().AsNoTracking().Where(item => item.SiteId == id).ToListAsync(cancellationToken);
        return [.. rows.OrderBy(row => row.Position).ThenBy(row => row.Code, StringComparer.Ordinal)];
    }

    private static SiteLocale Locale(List<SiteLocale> rows, string code) =>
        (LocaleTag.TryNormalize(code, out var normalized) ? rows.Find(row => string.Equals(row.Code, normalized, StringComparison.OrdinalIgnoreCase)) : null)
            ?? throw new ValidationException([new ValidationError("code", LocaleErrorCodes.LocaleNotFound, $"The site has no locale '{code}'.")]);

    private static bool IsDefault(Site site, SiteLocale row) => string.Equals(site.DefaultLocale, row.Code, StringComparison.OrdinalIgnoreCase);

    // Null for the implicit chain. Each code is another locale of the site, named once.
    private static List<string>? Fallbacks(IReadOnlyList<string>? fallbacks, string locale, List<SiteLocale> rows, List<ValidationError> errors)
    {
        if (fallbacks is null)
        {
            return null;
        }

        List<string> chain = [];
        foreach (var code in fallbacks)
        {
            var known = LocaleTag.TryNormalize(code, out var normalized)
                ? rows.Find(row => string.Equals(row.Code, normalized, StringComparison.OrdinalIgnoreCase))
                : null;
            if (known is null || string.Equals(known.Code, locale, StringComparison.OrdinalIgnoreCase) || chain.Contains(known.Code, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add(new("fallbacks", LocaleErrorCodes.LocaleNotAllowed, $"A fallback is another locale of the site, named once, and '{code}' is not."));
                continue;
            }

            chain.Add(known.Code);
        }

        return chain;
    }

    // What happens to a site is that site's.
    private EventSource SourceOf(Guid site) =>
        (accessor.Current is { } operation ? EventSource.From(operation) : EventSource.FromSystem(time.GetUtcNow())) with { SiteId = site };

    private async Task LogAsync(string action, Site site, string locale, CancellationToken cancellationToken) =>
        await activity.WriteAsync(new ActivityEntry(action) { TargetType = "site", TargetId = site.Id.ToString("D"), TargetLabel = site.Slug, Locale = locale }, SourceOf(site.Id), cancellationToken);

    private static SiteInfo Info(Site site) => new(site.Id, site.Name, site.Slug, site.DefaultLocale, site.IsDefault, site.CreatedAt);

    private static SiteLocaleInfo Info(SiteLocale row) => new(row.Code, row.Position, Chain(row.Fallbacks), row.Enabled);

    private static List<string>? Chain(string? json)
    {
        if (json is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<string?>>(json)?.OfType<string>().ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
