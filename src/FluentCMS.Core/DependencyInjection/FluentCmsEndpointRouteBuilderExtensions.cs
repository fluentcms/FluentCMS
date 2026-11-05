using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.DependencyInjection;

public static class FluentCmsEndpointRouteBuilderExtensions
{
    // The routes are relative to the request's PathBase, so a host on a sub-path calls UsePathBase before routing.
    // The returned builder applies conventions to every FluentCMS endpoint.
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

        return cms;
    }

    // The admin application calls /connect and reads the discovery document from its own origin, so those groups answer
    // the Management origins.
    private static RouteGroupBuilder Protect(RouteGroupBuilder routes, FluentCmsEndpointGroup group)
    {
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
}
