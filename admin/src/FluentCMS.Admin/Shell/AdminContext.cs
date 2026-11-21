using System.Net;
using System.Text.Json;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Interop;
using FluentCMS.Admin.Localization;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Admin.Shell;

internal enum ShellStatus
{
    Loading,
    Ready,

    // The user holds a role in no site.
    NoSite,

    // The server did not answer, or answered with an error.
    Failed,
}

// What the shell reads after sign-in (spec 4.1): GET /me, /me/sites and /me/preferences, whose formats apply from then on,
// then for the chosen site /me/access, /schema and /plugins. The site chosen last is kept for the tab, so that a reload stays
// in it. The content locale is the site's default until an address names another (spec 4.17), and stays until one names
// another again; /me/access and /schema are read again in it, since the server narrows both by the locale, and a site switch
// resets it. A read that a later switch overtook is dropped.
internal sealed class AdminContext(
    ManagementClient client,
    SiteMetadata metadata,
    ShellSite site,
    FormatService formats,
    ISessionStorage storage,
    ILogger<AdminContext> logger) : IAdminContext, IAdminAccess
{
    internal const string SchemaRead = "schema.read";

    private Task? _starting;
    private int _generation;
    private Task _localeReads = Task.CompletedTask;
    private Task<IReadOnlyDictionary<string, MyAccessInfo>>? _localeAccess;

    // The content locale AccessInfo was read in, null for the site's default.
    private string? _accessLocale;

    public event Action? Changed;

    public ShellStatus Status { get; private set; } = ShellStatus.Loading;

    public MeResponse? User { get; private set; }

    public IReadOnlyList<MySiteInfo> Sites { get; private set; } = [];

    public MySiteInfo? Site => site.Current;

    public string? ContentLocale => site.Locale ?? Site?.DefaultLocale;

    // The content locale when it is not the site's default; null otherwise.
    public string? NamedLocale => site.Locale;

    // The enabled locales of the site, in its order: those a view may be in, and that the badges of a localized type follow.
    public IReadOnlyList<string> ContentLocales =>
        [.. (Site?.Locales ?? []).Where(locale => locale.Enabled).OrderBy(locale => locale.Position).Select(locale => locale.Code)];

    public IAdminAccess Access => this;

    public MyAccessInfo? AccessInfo { get; private set; }

    // Whether AccessInfo is that of the content locale: not after a change of the locale until its access is read, nor while
    // a site is read, so what the user may do in the locale is not known meanwhile, and a gate holds what it decides.
    public bool AccessCurrent => AccessFor(null);

    // Whether AccessInfo is that of the content locale an address names, as UseLocaleAsync chooses it, or with null of the
    // content locale: an address that names another locale is read with its access only once UseLocaleAsync has read it.
    public bool AccessFor(string? locale) =>
        AccessInfo is not null && string.Equals(_accessLocale, locale is null ? site.ContentLocale : Chosen(locale), StringComparison.Ordinal);

    // Null when the user may not read the schema of the site; SchemaRefused says so.
    public SchemaDescription? Schema { get; private set; }

    public bool SchemaRefused { get; private set; }

    public IReadOnlyList<PluginInfo> Plugins { get; private set; } = [];

    // Whether the site answers a search: an index is registered and the search plugin, when it is the one, is not disabled
    // there (FCMS2211).
    public bool SearchIndex => AccessInfo?.Search == true && !Plugins.Any(plugin => plugin.Name == "search" && !plugin.Enabled);

    // Once per page load; after a failure the next call reads again.
    public Task StartAsync()
    {
        if (_starting is null || (_starting.IsCompleted && Status == ShellStatus.Failed))
        {
            var generation = ++_generation;
            Status = ShellStatus.Loading;
            Changed?.Invoke();
            _starting = RunAsync(generation, () => StartReadsAsync(generation));
        }

        return _starting;
    }

    // The generation is taken first, so that a later switch, even one back to the site before, overtakes this one.
    public async Task SwitchSiteAsync(Guid id)
    {
        if (Sites.FirstOrDefault(item => item.Id == id) is not { } chosen || chosen.Id == Site?.Id)
        {
            return;
        }

        var generation = ++_generation;
        Enter(chosen);
        await RunAsync(generation, async () =>
        {
            await storage.SetAsync(TabStorageKeys.Site, chosen.Id.ToString("D"));
            await SiteReadsAsync(generation);
        });
    }

    // After a change of a site or its locales (spec 4.16), so that the switchers follow. The site shown takes its new name and
    // locales; its access and schema are read again when its default locale changed, or when the content locale is no longer
    // one it has enabled, which then goes back to the default. A site that is gone, as one deleted, is left for the default
    // site, or the first, as a switch does. A read that fails, or that a switch or a change of locale overtook, changes nothing.
    public async Task ReadSitesAsync()
    {
        if (Status != ShellStatus.Ready)
        {
            return;
        }

        var generation = _generation;
        IReadOnlyList<MySiteInfo> mine;
        try
        {
            mine = await client.GetMySitesAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is ApiProblemException or HttpRequestException or JsonException or TaskCanceledException)
        {
            ShellLog.SitesNotRead(logger, exception);
            return;
        }

        if (generation != _generation || Site is not { } shown)
        {
            return;
        }

        Sites = mine;
        if (mine.FirstOrDefault(item => item.Id == shown.Id) is not { } same)
        {
            await LeaveAsync(mine);
            return;
        }

        var (locale, enabled) = (site.ContentLocale, ContentLocales);
        site.Current = same;
        if (!enabled.SequenceEqual(ContentLocales, StringComparer.Ordinal))
        {
            _localeAccess = null;
        }

        var chosen = locale is null ? null : Chosen(locale);
        if (chosen != locale || !string.Equals(same.DefaultLocale, shown.DefaultLocale, StringComparison.Ordinal))
        {
            var next = ++_generation;
            site.ContentLocale = chosen;
            Changed?.Invoke();
            await (_localeReads = RunAsync(next, () => SiteReadsAsync(next)));
            return;
        }

        Changed?.Invoke();
    }

    private Task LeaveAsync(IReadOnlyList<MySiteInfo> mine)
    {
        if (mine.Count > 0)
        {
            return SwitchSiteAsync((mine.FirstOrDefault(item => item.IsDefault) ?? mine[0]).Id);
        }

        ++_generation;
        (site.Current, site.ContentLocale, _localeAccess) = (null, null, null);
        (AccessInfo, Schema, SchemaRefused, Plugins) = (null, null, false, []);
        Status = ShellStatus.NoSite;
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    // The content locale an address names: one of the site's enabled locales, else the site's default; null keeps the one
    // there is. The access and the schema of the locale before stay until those of the new one are read, so a page awaits
    // this before it reads.
    public Task UseLocaleAsync(string? locale)
    {
        if (Status != ShellStatus.Ready || Site is not { } current)
        {
            return Task.CompletedTask;
        }

        if (locale is null)
        {
            return _localeReads;
        }

        var chosen = Chosen(locale);
        if (string.Equals(chosen, site.ContentLocale, StringComparison.Ordinal))
        {
            return _localeReads;
        }

        var generation = ++_generation;
        site.ContentLocale = chosen;
        return _localeReads = RunAsync(generation, () => SiteReadsAsync(generation));
    }

    // The content locale an address that names a locale is in: one of the site's enabled locales, null for its default, and
    // the default for a locale the site does not have enabled.
    private string? Chosen(string locale)
    {
        var chosen = ContentLocales.FirstOrDefault(code => string.Equals(code, locale, StringComparison.OrdinalIgnoreCase));
        return string.Equals(chosen, Site?.DefaultLocale, StringComparison.OrdinalIgnoreCase) ? null : chosen;
    }

    // The locales of the site in which the user may read the type: a grant may be limited to locales, and a read of every
    // locale leaves out those it may not read, which would otherwise look untranslated. A locale whose access could not be
    // read counts as readable.
    public async Task<IReadOnlyList<string>> ReadableLocalesAsync(string type)
    {
        var access = await LocaleAccessAsync();
        return [.. ContentLocales.Where(code => !access.TryGetValue(code, out var found) || found.Types.ContainsKey(type))];
    }

    // The other locales of the site in which the user may read some type, for a locale that shows none.
    public async Task<IReadOnlyList<string>> LocalesWithContentAsync()
    {
        var access = await LocaleAccessAsync();
        return [.. ContentLocales.Where(code => code != ContentLocale && access.TryGetValue(code, out var found) && found.Types.Count > 0)];
    }

    // /me/access in each enabled locale of the site, read once per site when a view first needs it.
    private Task<IReadOnlyDictionary<string, MyAccessInfo>> LocaleAccessAsync() =>
        _localeAccess ??= ReadLocaleAccessAsync(site.Site, ContentLocales);

    private async Task<IReadOnlyDictionary<string, MyAccessInfo>> ReadLocaleAccessAsync(string? siteId, IReadOnlyList<string> codes)
    {
        var reads = await Task.WhenAll(codes.Select(async code =>
        {
            try
            {
                return (Code: code, Access: (MyAccessInfo?)await client.GetMyAccessAsync(siteId, code, CancellationToken.None));
            }
            catch (Exception exception) when (exception is ApiProblemException or HttpRequestException or JsonException or TaskCanceledException)
            {
                return (Code: code, Access: null);
            }
        }));
        return reads.Where(read => read.Access is not null).ToDictionary(read => read.Code, read => read.Access!, StringComparer.Ordinal);
    }

    public bool Allows(string permission) => AccessInfo is { } access && Holds(access, permission);

    public bool AllowsForInstance(string permission) => AccessInfo is { } access && access.InstancePermissions.Contains(permission, StringComparer.Ordinal);

    // Whether the user may open an area of core's administration pages: it holds the area's permission, for the instance
    // when the area is instance-only. A permission with a star, as the settings menu's, is held with any name in its place.
    public bool Allows(AdministrationArea area)
    {
        ArgumentNullException.ThrowIfNull(area);
        if (AccessInfo is not { } access)
        {
            return false;
        }

        var held = area.InstanceOnly ? access.InstancePermissions : [.. access.Permissions, .. access.InstancePermissions];
        var star = area.Permission.IndexOf('*', StringComparison.Ordinal);
        if (star < 0)
        {
            return held.Contains(area.Permission, StringComparer.Ordinal);
        }

        var (start, end) = (area.Permission[..star], area.Permission[(star + 1)..]);
        return held.Any(name => name.Length > start.Length + end.Length && name.StartsWith(start, StringComparison.Ordinal) && name.EndsWith(end, StringComparison.Ordinal));
    }

    // Whether the user holds the permission of a page of an admin half, for the instance when the page is instance-only.
    // A page that names none is open to everyone, but an instance-only one, which has nothing to hold for the instance.
    public bool Allows(string? permission, bool instanceOnly) =>
        permission is null ? !instanceOnly : instanceOnly ? AllowsForInstance(permission) : Allows(permission);

    public TypeAccessInfo? ForType(string type) => AccessInfo is { } access && access.Types.TryGetValue(type, out var found) ? found : null;

    // /schema shows only the types the user may read; the access says so too, and both are asked.
    public bool MayRead(string type) => ForType(type) is not null;

    public bool PluginEnabled(string name) => Plugins.Any(plugin => plugin.Enabled && string.Equals(plugin.Name, name, StringComparison.Ordinal));

    // A role the user holds in the site or in every site, by name in any case, as the server compares role names; a key or a
    // service account holds none.
    public bool HasRole(string role) =>
        User?.Roles.Any(held => string.Equals(held.Name, role, StringComparison.OrdinalIgnoreCase) && (held.SiteId is null || held.SiteId == Site?.Id)) == true;

    private static bool Holds(MyAccessInfo access, string permission) =>
        access.Permissions.Contains(permission, StringComparer.Ordinal) || access.InstancePermissions.Contains(permission, StringComparer.Ordinal);

    private async Task StartReadsAsync(int generation)
    {
        var me = client.GetMeAsync(CancellationToken.None);
        var sites = client.GetMySitesAsync(CancellationToken.None);
        var preferences = ReadPreferencesAsync();
        var user = await me;
        var mine = await sites;
        var chosen = await preferences;
        if (generation != _generation)
        {
            return;
        }

        if (chosen is not null)
        {
            formats.Apply(chosen.DateFormat, chosen.NumberFormat, chosen.TimeZone);
        }

        (User, Sites) = (user, mine);
        if (mine.Count == 0)
        {
            Status = ShellStatus.NoSite;
            return;
        }

        var remembered = await storage.GetAsync(TabStorageKeys.Site);
        if (generation != _generation)
        {
            return;
        }

        Enter(mine.FirstOrDefault(item => string.Equals(item.Id.ToString("D"), remembered, StringComparison.OrdinalIgnoreCase))
            ?? mine.FirstOrDefault(item => item.IsDefault)
            ?? mine[0]);
        await SiteReadsAsync(generation);
    }

    // A key or a service account has no preferences, and a failed read leaves the browser's formats: neither stops the shell.
    private async Task<MyPreferences?> ReadPreferencesAsync()
    {
        try
        {
            return await client.GetMyPreferencesAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is ApiProblemException or HttpRequestException or JsonException or TaskCanceledException)
        {
            return null;
        }
    }

    // From here the client sends the new site in its default locale, and SiteMetadata drops what it read for the one before.
    // Nothing read for the site before is kept, so that Allows answers false until the new site's access is read.
    private void Enter(MySiteInfo chosen)
    {
        (site.Current, site.ContentLocale, _localeAccess) = (chosen, null, null);
        (AccessInfo, Schema, SchemaRefused, Plugins) = (null, null, false, []);
        Status = ShellStatus.Loading;
        Changed?.Invoke();
    }

    private async Task SiteReadsAsync(int generation)
    {
        var locale = site.ContentLocale;
        var access = await client.GetMyAccessAsync(site.Site, locale, CancellationToken.None);
        var plugins = metadata.GetPluginsAsync(CancellationToken.None);
        SchemaDescription? schema = null;
        var refused = !Holds(access, SchemaRead);
        if (!refused)
        {
            try
            {
                schema = await metadata.GetSchemaAsync(CancellationToken.None);
            }
            catch (ApiProblemException problem) when (problem.Problem.Status == (int)HttpStatusCode.Forbidden)
            {
                refused = true;
            }
        }

        var installed = await plugins;
        if (generation != _generation)
        {
            return;
        }

        (AccessInfo, _accessLocale, Schema, SchemaRefused, Plugins) = (access, locale, schema, refused, installed);
        Status = ShellStatus.Ready;
    }

    private async Task RunAsync(int generation, Func<Task> reads)
    {
        try
        {
            await reads();
        }
        // No cancellation is asked for, so a TaskCanceledException is the client's timeout.
        catch (Exception exception) when (exception is ApiProblemException or HttpRequestException or JsonException or TaskCanceledException)
        {
            ShellLog.StartFailed(logger, exception);
            if (generation == _generation)
            {
                Status = ShellStatus.Failed;
            }
        }

        if (generation == _generation)
        {
            Changed?.Invoke();
        }
    }
}
