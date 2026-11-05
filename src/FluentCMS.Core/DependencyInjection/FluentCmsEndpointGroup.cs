using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.DependencyInjection;

// The route groups of Appendix B that MapFluentCms creates.
internal enum FluentCmsEndpointGroup
{
    Delivery,
    Management,
    Connect,
    Account,
    Health,
}

internal sealed record FluentCmsEndpoints(FluentCmsEndpointGroup Group, Action<RouteGroupBuilder> Map);

internal static class FluentCmsEndpointGroups
{
    public static readonly IReadOnlyList<(FluentCmsEndpointGroup Group, string Prefix)> Prefixes =
    [
        (FluentCmsEndpointGroup.Delivery, "/api/delivery/v1"),
        (FluentCmsEndpointGroup.Management, "/api/management/v1"),
        (FluentCmsEndpointGroup.Connect, "/connect"),
        (FluentCmsEndpointGroup.Account, "/account"),
        (FluentCmsEndpointGroup.Health, "/health"),
    ];

    // How an area's registration puts its endpoints in a group; MapFluentCms maps them in the order they were added.
    public static IServiceCollection AddFluentCmsEndpoints(this IServiceCollection services, FluentCmsEndpointGroup group, Action<RouteGroupBuilder> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        return services.AddSingleton(new FluentCmsEndpoints(group, map));
    }
}
