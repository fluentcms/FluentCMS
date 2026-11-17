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

    public event Action? Changed;

    public ShellStatus Status { get; private set; } = ShellStatus.Loading;

    public MeResponse? User { get; private set; }

    public IReadOnlyList<MySiteInfo> Sites { get; private set; } = [];

    public MySiteInfo? Site => site.Current;

    public string? ContentLocale => site.Locale ?? Site?.DefaultLocale;

    // The content locale when it is not the site's default, as the address of a content page names it; null otherwise.
    public string? NamedLocale => site.Locale;

    // The enabled locales of the site, in its order: those a view may be in, and that the badges of a localized type follow.
    public IReadOnlyList<string> ContentLocales =>
        [.. (Site?.Locales ?? []).Where(locale => locale.Enabled).OrderBy(locale => locale.Position).Select(locale => locale.Code)];

    public IAdminAccess Access => this;

    public MyAccessInfo? AccessInfo { get; private set; }

    // Null when the user may not read the schema of the site; SchemaRefused says so.
    public SchemaDescription? Schema { get; private set; }

    public bool SchemaRefused { get; private set; }

    public IReadOnlyList<PluginInfo> Plugins { get; private set; } = [];

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

        var chosen = ContentLocales.FirstOrDefault(code => string.Equals(code, locale, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(chosen, current.DefaultLocale, StringComparison.OrdinalIgnoreCase))
        {
            chosen = null;
        }

        if (string.Equals(chosen, site.ContentLocale, StringComparison.Ordinal))
        {
            return _localeReads;
        }

        var generation = ++_generation;
        site.ContentLocale = chosen;
        return _localeReads = RunAsync(generation, () => SiteReadsAsync(generation));
    }

    public bool Allows(string permission) => AccessInfo is { } access && Holds(access, permission);

    public TypeAccessInfo? ForType(string type) => AccessInfo is { } access && access.Types.TryGetValue(type, out var found) ? found : null;

    // /schema shows only the types the user may read; the access says so too, and both are asked.
    public bool MayRead(string type) => ForType(type) is not null;

    public bool PluginEnabled(string name) => Plugins.Any(plugin => plugin.Enabled && string.Equals(plugin.Name, name, StringComparison.Ordinal));

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
        (site.Current, site.ContentLocale) = (chosen, null);
        (AccessInfo, Schema, SchemaRefused, Plugins) = (null, null, false, []);
        Status = ShellStatus.Loading;
        Changed?.Invoke();
    }

    private async Task SiteReadsAsync(int generation)
    {
        var access = await client.GetMyAccessAsync(site.Site, site.Locale, CancellationToken.None);
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

        (AccessInfo, Schema, SchemaRefused, Plugins) = (access, schema, refused, installed);
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
