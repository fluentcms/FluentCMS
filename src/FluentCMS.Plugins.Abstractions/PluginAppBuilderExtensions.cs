using Microsoft.AspNetCore.Routing;

namespace FluentCMS.Plugins;

/// <summary>The endpoints a plugin maps in <see cref="IPlugin.Configure"/>.</summary>
public static class PluginAppBuilderExtensions
{
    /// <summary>
    /// A group under the route group of FluentCMS, such as <c>/media</c>, behind the path base. Its endpoints run inside the
    /// operation of their request, with the principal of the FluentCMS authentication scheme, the Delivery rate limit and
    /// CORS policy, and no output cache unless an endpoint adds one. They need a signed-in caller, a bearer token or an API
    /// key, and answer 401 to any other; an endpoint that is public on purpose opts out with <c>AllowAnonymous()</c>.
    /// What the caller may do is still the plugin's to check, with <c>IPermissionChecker</c>. A prefix under a group of
    /// core, such as <c>/api/management/v1</c>, stops the host; <see cref="MapManagement"/> is for the Management API and
    /// <see cref="MapDelivery"/> for the Delivery API.
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

    /// <summary>
    /// A group under <c>/api/delivery/v1</c>, such as <c>/media</c> for <c>/api/delivery/v1/media</c>. Its endpoints get
    /// what the Delivery API has: the operation of their request, with the site and locale it names, an anonymous caller
    /// read as the Public role of its site, the Delivery rate limit, CORS policy (GET and HEAD) and output cache, and a place
    /// in the OpenAPI document. The output cache keeps a 200 answer to a GET or HEAD without credentials, or with an API key,
    /// by path, query, site and locale header and caller, and lets it go when its site is updated or a role changes; an
    /// endpoint whose answer depends on anything else opts out with <c>CacheOutput(policy =&gt; policy.NoCache())</c>. The
    /// endpoint sets its own <c>Cache-Control</c>. What the caller may read is the plugin's to check, with
    /// <c>IPermissionChecker</c>. A first segment that is a route parameter or the name of a content type stops the host.
    /// </summary>
    /// <param name="app">The builder of the plugin.</param>
    /// <param name="prefix">The prefix, starting with a slash.</param>
    /// <returns>The group.</returns>
    public static RouteGroupBuilder MapDelivery(this IPluginAppBuilder app, string prefix) => Endpoints(app).MapDelivery(prefix);

    private static IPluginEndpointBuilder Endpoints(IPluginAppBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app as IPluginEndpointBuilder
            ?? throw new InvalidOperationException($"{app.GetType().FullName} is not the plugin builder of a FluentCMS host, which is the only one that maps endpoints.");
    }
}
