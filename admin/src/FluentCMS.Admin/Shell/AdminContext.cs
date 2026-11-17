using System.Net;
using System.Text.Json;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Interop;
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

// What the shell reads after sign-in (spec 4.1): GET /me and /me/sites, then for the chosen site /me/access, /schema and
// /plugins. The site chosen last is kept for the tab, so that a reload stays in it. A read that a later switch overtook is
// dropped.
internal sealed class AdminContext(
    ManagementClient client,
    SiteMetadata metadata,
    ShellSite site,
    ISessionStorage storage,
    ILogger<AdminContext> logger) : IAdminContext, IAdminAccess
{
    internal const string SchemaRead = "schema.read";

    private Task? _starting;
    private int _generation;

    public event Action? Changed;

    public ShellStatus Status { get; private set; } = ShellStatus.Loading;

    public MeResponse? User { get; private set; }

    public IReadOnlyList<MySiteInfo> Sites { get; private set; } = [];

    public MySiteInfo? Site => site.Current;

    public string? ContentLocale => Site?.DefaultLocale;

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
        var user = await me;
        var mine = await sites;
        if (generation != _generation)
        {
            return;
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

    // From here the client sends the new site, and SiteMetadata drops what it read for the one before. Nothing read for the
    // site before is kept, so that Allows answers false until the new site's access is read.
    private void Enter(MySiteInfo chosen)
    {
        site.Current = chosen;
        (AccessInfo, Schema, SchemaRefused, Plugins) = (null, null, false, []);
        Status = ShellStatus.Loading;
        Changed?.Invoke();
    }

    private async Task SiteReadsAsync(int generation)
    {
        var access = await client.GetMyAccessAsync(site.Site, CancellationToken.None);
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
