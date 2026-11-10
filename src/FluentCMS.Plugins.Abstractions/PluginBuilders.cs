using System.Reflection;
using Microsoft.AspNetCore.Routing;

namespace FluentCMS.Plugins;

// Core's builders implement these, and the extension methods are how a plugin reaches them.
internal interface IPluginStorageBuilder
{
    void AddModel(IPluginModelContributor contributor);

    void AddMigrations(Assembly assembly);
}

// The name of the rate limit policy that RequireCallerRateLimit names and core registers.
internal static class PluginRateLimits
{
    public const string CallerPolicy = "caller";
}

internal interface IPluginEndpointBuilder
{
    RouteGroupBuilder MapGroup(string prefix);

    RouteGroupBuilder MapManagement(string prefix);

    RouteGroupBuilder MapDelivery(string prefix);

    RouteGroupBuilder MapProtectedResource(string prefix, ProtectedResourceOptions resource);
}
