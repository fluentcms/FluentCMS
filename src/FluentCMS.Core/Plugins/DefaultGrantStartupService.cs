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
        var defaults = Declared();
        if (defaults.Count == 0)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        foreach (var (role, permissions) in await scope.ServiceProvider.GetRequiredService<RoleService>().ApplyDefaultsAsync(defaults, cancellationToken))
        {
            var given = string.Join(", ", permissions);
            PluginLog.DefaultGrantsGiven(logger, role, given);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // Each default with the plugin whose provider declares it, none for a provider the host registered itself.
    private List<DefaultGrant> Declared()
    {
        Dictionary<Type, string> owners = [];
        foreach (var plugin in registry?.Plugins ?? [])
        {
            foreach (var type in registry!.Contributions.PermissionProvidersOf(plugin.Name))
            {
                owners.TryAdd(type, plugin.Name);
            }
        }

        return [.. providers.SelectMany(provider => provider.GetPermissions().SelectMany(permission => permission.DefaultRoles
            .Select(role => new DefaultGrant(BuiltInRoles.Of(role), permission.Name, owners.GetValueOrDefault(provider.GetType())))))];
    }
}
