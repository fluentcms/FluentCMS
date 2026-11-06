using FluentCMS.Core.Api.Account;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Api.Delivery;
using FluentCMS.Core.Api.OpenApi;
using FluentCMS.Core.Plugins;
using FluentCMS.Core.Storage;
using Microsoft.AspNetCore.Builder;
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

        foreach (var plugin in endpoints.ServiceProvider.GetRequiredService<PluginRegistry>().Plugins)
        {
            plugin.Instance.Configure(new PluginAppBuilder(plugin.Name, endpoints.ServiceProvider));
        }

        return new Conventions(cms, HostedPages.Map(endpoints));
    }

    // /openapi/v1.json and the Scalar UI at /scalar, which a host maps itself, so they are where it puts them: not held to
    // the path base, and under any group it maps them in. FluentCms:OpenApi:Enabled turns them on outside Development; off,
    // nothing is mapped. The returned builder applies conventions to both, such as RequireAuthorization.
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

        var document = endpoints.MapOpenApi(OpenApiRegistration.DocumentRoute);
        var scalar = endpoints.MapScalarApiReference(OpenApiRegistration.ScalarRoute, (ui, context) =>
            ui.WithTitle("FluentCMS API").WithOpenApiRoutePattern(OpenApiRegistration.DocumentUrl(context)));
        return new Conventions(document, scalar);
    }

    // The admin application calls /connect and reads the discovery document from its own origin, so those groups answer
    // the Management origins. Only Delivery is cached; the others say so, so that a base policy of the host's leaves them alone.
    private static RouteGroupBuilder Protect(RouteGroupBuilder routes, FluentCmsEndpointGroup group)
    {
        // Discovery and the key set belong to the authorization server, so they count as its surface.
        var surface = group == FluentCmsEndpointGroup.WellKnown ? FluentCmsEndpointGroup.Connect : group;
        routes.WithMetadata(new FluentCmsSurfaceMetadata(surface.ToString().ToLowerInvariant()));
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
