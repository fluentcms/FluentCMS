using FluentCMS.Core.Storage;
using Microsoft.AspNetCore.Builder;
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

        var registered = endpoints.ServiceProvider.GetServices<FluentCmsEndpoints>().ToList();
        var cms = endpoints.MapGroup(string.Empty);
        foreach (var (group, prefix) in FluentCmsEndpointGroups.Prefixes)
        {
            var routes = cms.MapGroup(prefix);
            foreach (var area in registered.Where(area => area.Group == group))
            {
                area.Map(routes);
            }
        }

        return cms;
    }
}
