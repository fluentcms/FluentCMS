using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Api.OpenApi;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Api.Management;

// The plugins group of spec 4.10: the plugin list, which every caller of the Management API reads, and enabling or disabling a
// plugin for a site, which needs plugins.manage for that site.
internal static class PluginEndpoints
{
    public static IServiceCollection AddManagementPluginApi(this IServiceCollection services) =>
        services.AddFluentCmsEndpoints(FluentCmsEndpointGroup.Management, Map);

    private static void Map(RouteGroupBuilder management)
    {
        var plugins = management.MapGroup("/plugins").WithFluentCmsOperation(Origin.Api).RequireManagementScope();
        plugins.MapGet("", ListAsync).Produces<ItemList<PluginInfo>>();
        plugins.MapPut("/{name}/sites/{site}", SetEnabledAsync).Takes<PluginSiteRequest>().Produces<PluginSiteInfo>();
    }

    private static async Task<IResult> ListAsync(HttpContext httpContext, [FromServices] PluginAdministration plugins)
    {
        ManagementQuery.Only(httpContext.Request);
        return AdminJson.Items(await plugins.ListAsync(httpContext.RequestAborted));
    }

    private static async Task<IResult> SetEnabledAsync(string name, string site, HttpContext httpContext, [FromServices] PluginAdministration plugins)
    {
        ManagementQuery.Only(httpContext.Request);
        var siteId = ManagementQuery.RouteId(site, nameof(site));
        var body = await BodyReader.ReadAsync(httpContext.Request, "enabled");
        var enabled = body.Boolean("enabled", required: true);
        body.ThrowIfInvalid();
        return AdminJson.Ok(await plugins.SetEnabledAsync(name, siteId, enabled!.Value, httpContext.RequestAborted));
    }
}
