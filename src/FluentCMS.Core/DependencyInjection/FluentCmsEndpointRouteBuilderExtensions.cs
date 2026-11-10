using FluentCMS.Content;
using FluentCMS.Core.Api.Account;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Api.Delivery;
using FluentCMS.Core.Api.OpenApi;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.Plugins;
using FluentCMS.Core.Storage;
using FluentCMS.Plugins;
using FluentCMS.Schema;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scalar.AspNetCore;

namespace FluentCMS.Core.DependencyInjection;

public static class FluentCmsEndpointRouteBuilderExtensions
{
    // The routes are relative to the request's PathBase, which UseFluentCms sets from FluentCms:ReverseProxy:PathBase.
    // The returned builder applies conventions to every FluentCMS endpoint, the hosted pages included.
    public static IEndpointConventionBuilder MapFluentCms(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        if (endpoints.ServiceProvider.GetService<IDataProvider>() is null)
        {
            throw new FluentCmsConfigurationException(
                HostErrorCodes.NotRegistered,
                "MapFluentCms found no FluentCMS services. Call builder.Services.AddFluentCms(...) before building the app.");
        }

        if (!endpoints.ServiceProvider.GetRequiredService<ApiPipelineState>().Configured)
        {
            throw new FluentCmsConfigurationException(
                HostErrorCodes.PipelineMissing,
                "MapFluentCms found no FluentCMS pipeline. Call app.UseFluentCms() before app.MapFluentCms(); the endpoints need its CORS, rate limiting and request limits.");
        }

        var registered = endpoints.ServiceProvider.GetServices<FluentCmsEndpoints>().ToList();
        var cms = endpoints.MapGroup(string.Empty).WithMetadata(FluentCmsEndpointMetadata.Instance);
        foreach (var (group, prefix) in FluentCmsEndpointGroups.Prefixes)
        {
            var routes = Protect(cms.MapGroup(prefix), group);
            foreach (var area in registered.Where(area => area.Group == group))
            {
                area.Map(routes);
            }
        }

        var plugins = endpoints.ServiceProvider.GetRequiredService<PluginRegistry>();
        if (plugins.TryStartConfigure())
        {
            foreach (var plugin in plugins.Plugins)
            {
                plugin.Instance.Configure(new PluginAppBuilder(plugin, endpoints.ServiceProvider, cms));
            }
        }

        return new Conventions(cms, HostedPages.Map(endpoints));
    }

    // /openapi/v1.json and the Scalar UI at /scalar, which a host maps itself, so they are where it puts them: not held to
    // the path base, and under any group it maps them in. FluentCms:OpenApi:Enabled turns them on outside Development; off,
    // nothing is mapped. The returned builder applies conventions to both, such as RequireAuthorization. They are FluentCMS
    // endpoints of the docs surface, so they get its headers, the coarse limit before authentication and the Delivery limit
    // by client address, and they never look up a key, since nothing on them reads one.
    public static IEndpointConventionBuilder MapFluentCmsOpenApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetService<OpenApiDocumentOptions>() ?? throw new FluentCmsConfigurationException(
            HostErrorCodes.NotRegistered,
            "MapFluentCmsOpenApi found no FluentCMS services. Call builder.Services.AddFluentCms(...) before building the app.");

        if (!(options.Enabled ?? endpoints.ServiceProvider.GetService<IHostEnvironment>()?.IsDevelopment() == true))
        {
            return new Conventions();
        }

        var document = endpoints.MapOpenApi(OpenApiRegistration.DocumentRoute).CacheOutput(OpenApiDocumentCachePolicy.Instance);
        var scalar = endpoints.MapScalarApiReference(OpenApiRegistration.ScalarRoute, ScalarPage.Configure).WithMetadata(ScalarPage.Policy);
        var docs = new Conventions(document, scalar);
        docs.WithMetadata(FluentCmsEndpointMetadata.Instance, FluentCmsSurfaceMetadata.Docs).RequireRateLimiting(RateLimitPolicyNames.Delivery);
        return docs;
    }

    // A plugin's own group, under the CMS group and outside those of core, so that nothing reaches the Management API without
    // its scope. Its surface is the plugin's, which keeps it out of the OpenAPI document of Delivery and Management. Its
    // endpoints need a signed-in caller unless one carries AllowAnonymous(), and the caller is checked before the plugin's
    // enablement, so that an anonymous caller learns nothing of a site's plugins.
    internal static RouteGroupBuilder MapPluginGroup(RouteGroupBuilder cms, RegisteredPlugin plugin, string prefix)
    {
        CheckPrefix(plugin, prefix, "MapGroup");

        // A first segment that is a parameter would match the paths of core's groups too.
        if (prefix[1] is '{' or '*')
        {
            throw new FluentCmsConfigurationException(
                PluginErrorCodes.InvalidEndpointPrefix,
                $"The plugin '{plugin.Name}' ({plugin.Type.FullName}) maps the group '{prefix}', whose first segment is a route parameter; a plugin's group starts with a name of its own, such as '/{plugin.Name}'.");
        }

        if (FluentCmsEndpointGroups.Prefixes.FirstOrDefault(group => Covers(group.Prefix, prefix)) is { Prefix: { } core })
        {
            throw new FluentCmsConfigurationException(
                PluginErrorCodes.InvalidEndpointPrefix,
                $"The plugin '{plugin.Name}' ({plugin.Type.FullName}) maps the group '{prefix}', which is under {core} of core; MapManagement maps a group under the Management API, and MapDelivery one under the Delivery API.");
        }

        var routes = cms.MapGroup(prefix).WithMetadata(new FluentCmsSurfaceMetadata(plugin.Name));
        routes.CacheOutput(policy => policy.NoCache()).RequireRateLimiting(RateLimitPolicyNames.Delivery).RequireCors(CorsPolicies.Delivery);
        return WhenEnabled(routes.WithFluentCmsOperation(Origin.Api).RequireSignedIn(), plugin);
    }

    internal static RouteGroupBuilder MapPluginManagement(RouteGroupBuilder cms, RegisteredPlugin plugin, string prefix)
    {
        CheckPrefix(plugin, prefix, "MapManagement");
        return WhenEnabled(
            Protect(cms.MapGroup(PrefixOf(FluentCmsEndpointGroup.Management)), FluentCmsEndpointGroup.Management)
                .MapGroup(prefix)
                .WithFluentCmsOperation(Origin.Api)
                .RequireManagementScope(),
            plugin);
    }

    // What core's Delivery group has, an anonymous caller read as its site's Public role included. A literal segment outranks
    // the {type} of core's routes, so a first segment that names a content type would take that type's reads, and one that
    // is a route parameter would match the same paths as core's.
    internal static RouteGroupBuilder MapPluginDelivery(RouteGroupBuilder cms, RegisteredPlugin plugin, string prefix, SchemaModel schema)
    {
        CheckPrefix(plugin, prefix, "MapDelivery");
        if (prefix[1] is '{' or '*')
        {
            throw new FluentCmsConfigurationException(
                PluginErrorCodes.InvalidEndpointPrefix,
                $"The plugin '{plugin.Name}' ({plugin.Type.FullName}) calls MapDelivery with '{prefix}', whose first segment is a route parameter; a plugin's group starts with a name of its own, such as '/{plugin.Name}'.");
        }

        var first = prefix[1..].Split('/')[0];
        if (schema.ContentTypes.FirstOrDefault(type => string.Equals(type.Name, first, StringComparison.OrdinalIgnoreCase)) is { } type)
        {
            throw new FluentCmsConfigurationException(
                PluginErrorCodes.InvalidEndpointPrefix,
                $"The plugin '{plugin.Name}' ({plugin.Type.FullName}) calls MapDelivery with '{prefix}', whose first segment is the content type '{type.Name}', whose Delivery routes it would take.");
        }

        return WhenEnabled(
            Protect(cms.MapGroup(PrefixOf(FluentCmsEndpointGroup.Delivery)), FluentCmsEndpointGroup.Delivery)
                .MapGroup(prefix)
                .WithFluentCmsOperation(Origin.Api),
            plugin);
    }

    // A plugin's own group that is an OAuth protected resource (RFC 9728), as /mcp is: what MapPluginGroup gives, with the
    // origin the plugin names, the Management limits, since it takes writes, and the scope of the resource asked of a bearer
    // token. Its metadata is a document of core's /.well-known group, which a plugin maps nothing under, and the registry
    // is what the authorization server reads to take the resource parameter.
    internal static RouteGroupBuilder MapPluginProtectedResource(RouteGroupBuilder cms, RegisteredPlugin plugin, string prefix, ProtectedResourceOptions options, ProtectedResources resources)
    {
        CheckPrefix(plugin, prefix, "MapProtectedResource");

        // The URL of the group names the resource, so it has no parameter anywhere.
        if (prefix.Contains('{', StringComparison.Ordinal) || prefix.Contains('*', StringComparison.Ordinal))
        {
            throw new FluentCmsConfigurationException(
                PluginErrorCodes.InvalidEndpointPrefix,
                $"The plugin '{plugin.Name}' ({plugin.Type.FullName}) calls MapProtectedResource with '{prefix}', which has a route parameter; the URL of a protected resource is fixed, such as '/{plugin.Name}'.");
        }

        if (FluentCmsEndpointGroups.Prefixes.FirstOrDefault(group => Covers(group.Prefix, prefix)) is { Prefix: { } core })
        {
            throw new FluentCmsConfigurationException(
                PluginErrorCodes.InvalidEndpointPrefix,
                $"The plugin '{plugin.Name}' ({plugin.Type.FullName}) calls MapProtectedResource with '{prefix}', which is under {core} of core.");
        }

        var problem = !AuthorizationServer.ResourceScopes.Contains(options.Scope, StringComparer.Ordinal)
                ? $"names the scope '{options.Scope}'; the scopes a protected resource may ask for are {string.Join(", ", AuthorizationServer.ResourceScopes)}"
            : options.Origin is not (Origin.Api or Origin.Mcp)
                ? $"names the origin {options.Origin}; a protected resource enters as {Origin.Api} or {Origin.Mcp}"
            : resources.All.FirstOrDefault(resource => string.Equals(resource.Prefix, prefix, StringComparison.OrdinalIgnoreCase)) is { } taken
                ? $"is a protected resource of the plugin '{taken.Plugin}' already"
            : null;
        if (problem is not null)
        {
            throw new FluentCmsConfigurationException(
                PluginErrorCodes.InvalidProtectedResource,
                $"The plugin '{plugin.Name}' ({plugin.Type.FullName}) calls MapProtectedResource with '{prefix}', which {problem}.");
        }

        var resource = new ProtectedResource(plugin.Name, prefix, options.Scope, options.Name);
        var wellKnown = Protect(cms.MapGroup(PrefixOf(FluentCmsEndpointGroup.WellKnown)), FluentCmsEndpointGroup.WellKnown);
        wellKnown.MapGet(ProtectedResources.MetadataPath + prefix, (HttpContext context) => Results.Json(resources.Document(context.Request, resource))).ExcludeFromDescription();

        // The document without a path is the one a client tries when it does not know the path of the resource. It answers
        // while the host has one protected resource, since it could describe no more than one.
        if (resources.All.Count == 0)
        {
            wellKnown.MapGet(ProtectedResources.MetadataPath, async (HttpContext context) =>
            {
                if (resources.All is [var only])
                {
                    await Results.Json(resources.Document(context.Request, only)).ExecuteAsync(context);
                    return;
                }

                await ProblemWriter.WriteAsync(context, ProblemMapper.Known(ApiErrorCodes.NotFound, "The host has several protected resources; the metadata of each is under its own path."), cancellationToken: context.RequestAborted);
            }).ExcludeFromDescription();
        }

        resources.Add(resource);

        var routes = cms.MapGroup(prefix).WithMetadata(new FluentCmsSurfaceMetadata(plugin.Name), new ProtectedResourceMetadata(resource));
        routes.CacheOutput(policy => policy.NoCache()).RequireRateLimiting(RateLimitPolicyNames.Management).RequireCors(CorsPolicies.Management);
        return WhenEnabled(routes.WithFluentCmsOperation(options.Origin).RequireResourceScope(options.Scope), plugin);
    }

    private static string PrefixOf(FluentCmsEndpointGroup group) => FluentCmsEndpointGroups.PrefixOf(group);

    // After the filter of the operation, whose site it reads, so that a plugin disabled in that site answers 404 there.
    private static RouteGroupBuilder WhenEnabled(RouteGroupBuilder routes, RegisteredPlugin plugin)
    {
        routes.WithMetadata(new PluginEndpointMetadata(plugin.Name)).AddEndpointFilter(async (context, next) =>
        {
            var httpContext = context.HttpContext;
            await httpContext.RequestServices.GetRequiredService<PluginState>()
                .RequireEnabledAsync(plugin.Name, ApiRequest.Of(httpContext).Operation.Site, httpContext.RequestAborted);
            return await next(context);
        });
        return routes;
    }

    private static void CheckPrefix(RegisteredPlugin plugin, string prefix, string method)
    {
        if (prefix is not ['/', _, ..] || prefix.EndsWith('/'))
        {
            throw new FluentCmsConfigurationException(
                PluginErrorCodes.InvalidEndpointPrefix,
                $"The plugin '{plugin.Name}' ({plugin.Type.FullName}) calls {method} with '{prefix}'; a prefix starts with a slash and names a path, such as '/{plugin.Name}'.");
        }
    }

    // Routing ignores case, so a group covers a prefix in any case.
    private static bool Covers(string group, string prefix) =>
        prefix.StartsWith(group, StringComparison.OrdinalIgnoreCase) && (prefix.Length == group.Length || prefix[group.Length] == '/');

    // The admin application calls /connect and reads the discovery document from its own origin, so those groups answer
    // the Management origins. Only Delivery is cached; the others say so, so that a base policy of the host's leaves them alone.
    private static RouteGroupBuilder Protect(RouteGroupBuilder routes, FluentCmsEndpointGroup group)
    {
        // Discovery and the key set belong to the authorization server, so they count as its surface.
        var surface = group == FluentCmsEndpointGroup.WellKnown ? FluentCmsEndpointGroup.Connect : group;
        routes.WithMetadata(group switch
        {
            FluentCmsEndpointGroup.Health => FluentCmsSurfaceMetadata.Health,
            FluentCmsEndpointGroup.Delivery => FluentCmsSurfaceMetadata.Delivery,
            _ => new FluentCmsSurfaceMetadata(surface.ToString().ToLowerInvariant()),
        });
        if (group == FluentCmsEndpointGroup.Delivery)
        {
            routes.CacheOutput(DeliveryOutputCachePolicy.Instance);
        }
        else
        {
            routes.CacheOutput(policy => policy.NoCache());
        }

        switch (group)
        {
            case FluentCmsEndpointGroup.Delivery:
                routes.RequireRateLimiting(RateLimitPolicyNames.Delivery).RequireCors(CorsPolicies.Delivery);
                break;
            case FluentCmsEndpointGroup.Management:
                routes.RequireRateLimiting(RateLimitPolicyNames.Management).RequireCors(CorsPolicies.Management);
                break;
            case FluentCmsEndpointGroup.Connect:
                routes.RequireRateLimiting(RateLimitPolicyNames.Auth).RequireCors(CorsPolicies.Management);
                break;
            case FluentCmsEndpointGroup.WellKnown:
                routes.RequireCors(CorsPolicies.Management);
                break;
            case FluentCmsEndpointGroup.Account:
                routes.AsHostedPages();
                break;
        }

        return routes;
    }

    private sealed class Conventions(params IEndpointConventionBuilder[] builders) : IEndpointConventionBuilder
    {
        public void Add(Action<EndpointBuilder> convention)
        {
            foreach (var builder in builders)
            {
                builder.Add(convention);
            }
        }

        public void Finally(Action<EndpointBuilder> finallyConvention)
        {
            foreach (var builder in builders)
            {
                builder.Finally(finallyConvention);
            }
        }
    }
}
