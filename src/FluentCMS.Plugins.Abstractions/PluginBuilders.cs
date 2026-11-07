using System.Reflection;
using Microsoft.AspNetCore.Routing;

namespace FluentCMS.Plugins;

// Core's builders implement these, and the extension methods are how a plugin reaches them.
internal interface IPluginStorageBuilder
{
    void AddModel(IPluginModelContributor contributor);

    void AddMigrations(Assembly assembly);
}

internal interface IPluginEndpointBuilder
{
    RouteGroupBuilder MapGroup(string prefix);

    RouteGroupBuilder MapManagement(string prefix);
}
