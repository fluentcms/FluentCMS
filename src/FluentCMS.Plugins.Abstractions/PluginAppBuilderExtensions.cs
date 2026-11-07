using Microsoft.AspNetCore.Routing;

namespace FluentCMS.Plugins;

/// <summary>The endpoints a plugin maps in <see cref="IPlugin.Configure"/>.</summary>
public static class PluginAppBuilderExtensions
{
    /// <summary>
    /// A group under the route group of FluentCMS, such as <c>/media</c>, behind the path base. Its endpoints run inside the
    /// operation of their request, with the principal of the FluentCMS authentication scheme, the Delivery rate limit and
    /// CORS policy, and no output cache unless an endpoint adds one. A prefix under a group of core, such as
    /// <c>/api/management/v1</c>, stops the host; <see cref="MapManagement"/> is for the Management API.
    /// </summary>
    /// <param name="app">The builder of the plugin.</param>
    /// <param name="prefix">The prefix, starting with a slash.</param>
    /// <returns>The group.</returns>
    public static RouteGroupBuilder MapGroup(this IPluginAppBuilder app, string prefix) => Endpoints(app).MapGroup(prefix);

    /// <summary>
    /// A group under <c>/api/management/v1</c>, such as <c>/media</c> for <c>/api/management/v1/media</c>. Its endpoints get
    /// what the Management API has: the operation of their request, a principal with the <c>management</c> scope, the
    /// Management rate limit and CORS policy, and a place in the OpenAPI document.
    /// </summary>
    /// <param name="app">The builder of the plugin.</param>
    /// <param name="prefix">The prefix, starting with a slash.</param>
    /// <returns>The group.</returns>
    public static RouteGroupBuilder MapManagement(this IPluginAppBuilder app, string prefix) => Endpoints(app).MapManagement(prefix);

    private static IPluginEndpointBuilder Endpoints(IPluginAppBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app as IPluginEndpointBuilder
            ?? throw new InvalidOperationException($"{app.GetType().FullName} is not the plugin builder of a FluentCMS host, which is the only one that maps endpoints.");
    }
}
