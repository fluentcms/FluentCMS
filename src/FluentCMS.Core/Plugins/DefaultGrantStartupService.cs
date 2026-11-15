using FluentCMS.Authorization;
using FluentCMS.Core.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Plugins;

// Gives the built-in roles the permissions their providers name by default (Permission.DefaultRoles) at each start, after
// the migrations and before the seed. RoleService applies only what the instance has not applied, so each default is
// given once.
internal sealed class DefaultGrantStartupService(
    IEnumerable<IPermissionProvider> providers,
    IServiceScopeFactory scopes,
    ILogger<DefaultGrantStartupService> logger,
    PluginRegistry? registry = null) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Dictionary<Type, string> owners = [];
        foreach (var plugin in registry?.Plugins ?? [])
        {
            foreach (var type in registry!.Contributions.PermissionProvidersOf(plugin.Name))
            {
                owners.TryAdd(type, plugin.Name);
            }
        }

        var declared = providers
            .SelectMany(provider => provider.GetPermissions().SelectMany(permission => permission.DefaultRoles.Select(role => (Role: BuiltInRoles.Of(role), permission.Name, Provider: provider.GetType()))))
            .ToList();
        if (declared.Count == 0)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var catalogue = scope.ServiceProvider.GetRequiredService<PermissionCatalogue>();
        List<DefaultGrant> defaults = [.. declared.Select(item =>
        {
            var plugin = owners.GetValueOrDefault(item.Provider);
            return new DefaultGrant(item.Role, item.Name, item.Provider, plugin, PermissionsOf(catalogue, item.Provider, plugin, owners));
        })];

        var outcome = await scope.ServiceProvider.GetRequiredService<RoleService>().ApplyDefaultsAsync(defaults, cancellationToken);
        foreach (var (role, permissions) in outcome.Given)
        {
            var given = string.Join(", ", permissions);
            PluginLog.DefaultGrantsGiven(logger, role, given);
        }

        foreach (var refused in outcome.Refused)
        {
            PluginLog.DefaultGrantRefused(logger, refused.Permission, refused.Provider.FullName ?? refused.Provider.Name);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // The permissions of the plugin that added the provider, or of the provider alone when the host registered it itself.
    private static HashSet<string> PermissionsOf(PermissionCatalogue catalogue, Type provider, string? plugin, Dictionary<Type, string> owners) =>
        [.. catalogue.Entries
            .Select(entry => entry.Name)
            .Where(name => catalogue.ProviderOf(name) is { } declaring && (plugin is null ? declaring == provider : owners.GetValueOrDefault(declaring) == plugin))];
}
