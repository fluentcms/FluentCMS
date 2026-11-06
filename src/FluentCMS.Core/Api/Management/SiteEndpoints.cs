using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Settings;
using FluentCMS.Core.Sites;
using FluentCMS.Sites;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Api.Management;

// The sites and settings groups of spec 4.10. Sites are read and changed, and their locales added, changed and removed, with
// sites.manage; creating and deleting sites, and hostnames, are the multi-site plugin's (Q6a). Settings are read and written
// per area with settings.{area}.manage: /settings/{area} for the site of the request and /settings/{area}/defaults for the
// instance defaults. A read is redacted, and a write names the version it read as If-Match.
internal static class SiteEndpoints
{
    public static IServiceCollection AddManagementSiteApi(this IServiceCollection services) =>
        services.AddFluentCmsEndpoints(FluentCmsEndpointGroup.Management, Map);

    private static void Map(RouteGroupBuilder management)
    {
        var sites = management.MapGroup("/sites").WithFluentCmsOperation(Origin.Api).RequireManagementScope();
        sites.MapGet("", ListAsync);
        sites.MapGet("/{id}", GetAsync);
        sites.MapPatch("/{id}", UpdateAsync);
        sites.MapGet("/{id}/locales", LocalesAsync);
        sites.MapPost("/{id}/locales", AddLocaleAsync);
        sites.MapPatch("/{id}/locales/{code}", UpdateLocaleAsync);
        sites.MapDelete("/{id}/locales/{code}", RemoveLocaleAsync);

        var settings = management.MapGroup("/settings/{area}").WithFluentCmsOperation(Origin.Api).RequireManagementScope();
        settings.MapGet("", (string area, HttpContext httpContext, [FromServices] SettingsService service) => ReadSettingAsync(area, instance: false, httpContext, service));
        settings.MapPut("", (string area, HttpContext httpContext, [FromServices] SettingsService service) => WriteSettingAsync(area, instance: false, httpContext, service));
        settings.MapGet("/defaults", (string area, HttpContext httpContext, [FromServices] SettingsService service) => ReadSettingAsync(area, instance: true, httpContext, service));
        settings.MapPut("/defaults", (string area, HttpContext httpContext, [FromServices] SettingsService service) => WriteSettingAsync(area, instance: true, httpContext, service));
    }

    private static async Task<IResult> ListAsync(HttpContext httpContext, [FromServices] SiteManager sites)
    {
        ManagementQuery.Only(httpContext.Request);
        return AdminJson.Items(await sites.ListAsync(httpContext.RequestAborted));
    }

    private static async Task<IResult> GetAsync(string id, HttpContext httpContext, [FromServices] SiteManager sites)
    {
        ManagementQuery.Only(httpContext.Request);
        return AdminJson.Ok(await sites.GetAsync(ManagementQuery.RouteId(id, nameof(id)), httpContext.RequestAborted));
    }

    private static async Task<IResult> UpdateAsync(string id, HttpContext httpContext, [FromServices] SiteManager sites)
    {
        ManagementQuery.Only(httpContext.Request);
        var siteId = ManagementQuery.RouteId(id, nameof(id));
        var body = await BodyReader.ReadAsync(httpContext.Request, "name", "defaultLocale");
        var change = new SiteChange { Name = body.String("name"), DefaultLocale = body.String("defaultLocale") };
        body.ThrowIfInvalid();
        return AdminJson.Ok(await sites.UpdateAsync(siteId, change, httpContext.RequestAborted));
    }

    private static async Task<IResult> LocalesAsync(string id, HttpContext httpContext, [FromServices] SiteManager sites)
    {
        ManagementQuery.Only(httpContext.Request);
        return AdminJson.Items(await sites.LocalesAsync(ManagementQuery.RouteId(id, nameof(id)), httpContext.RequestAborted));
    }

    private static async Task<IResult> AddLocaleAsync(string id, HttpContext httpContext, [FromServices] SiteManager sites)
    {
        ManagementQuery.Only(httpContext.Request);
        var siteId = ManagementQuery.RouteId(id, nameof(id));
        var body = await BodyReader.ReadAsync(httpContext.Request, "code", "position", "enabled", "fallbacks");
        var code = body.String("code", required: true);
        var position = body.Integer("position");
        var enabled = body.Boolean("enabled") ?? true;
        var fallbacks = body.Strings("fallbacks");
        body.ThrowIfInvalid();
        return AdminJson.Ok(await sites.AddLocaleAsync(siteId, code!, position, enabled, fallbacks, httpContext.RequestAborted), StatusCodes.Status201Created);
    }

    // fallbacks: null goes back to the implicit chain; leaving it out keeps the chain the locale has.
    private static async Task<IResult> UpdateLocaleAsync(string id, string code, HttpContext httpContext, [FromServices] SiteManager sites)
    {
        ManagementQuery.Only(httpContext.Request);
        var siteId = ManagementQuery.RouteId(id, nameof(id));
        var body = await BodyReader.ReadAsync(httpContext.Request, "position", "enabled", "fallbacks");
        var change = new SiteLocaleChange
        {
            Position = body.Integer("position"),
            Enabled = body.Boolean("enabled"),
            SetsFallbacks = body.Has("fallbacks"),
            Fallbacks = body.Strings("fallbacks"),
        };
        body.ThrowIfInvalid();
        return AdminJson.Ok(await sites.UpdateLocaleAsync(siteId, code, change, httpContext.RequestAborted));
    }

    private static async Task<IResult> RemoveLocaleAsync(string id, string code, HttpContext httpContext, [FromServices] SiteManager sites)
    {
        ManagementQuery.Only(httpContext.Request);
        await sites.RemoveLocaleAsync(ManagementQuery.RouteId(id, nameof(id)), code, httpContext.RequestAborted);
        return Results.NoContent();
    }

    private static async Task<IResult> ReadSettingAsync(string area, bool instance, HttpContext httpContext, SettingsService service)
    {
        ManagementQuery.Only(httpContext.Request);
        var descriptor = service.Require(area);
        var site = instance ? (SiteId?)null : ApiRequest.Of(httpContext).Operation.Site;
        return Setting(httpContext, descriptor, site, await service.ResolveRedactedAsync(descriptor, site, httpContext.RequestAborted));
    }

    // The body is the whole document; a secret it leaves out keeps the value that is stored.
    private static async Task<IResult> WriteSettingAsync(string area, bool instance, HttpContext httpContext, SettingsService service)
    {
        ManagementQuery.Only(httpContext.Request);
        var descriptor = service.Require(area);
        var site = instance ? (SiteId?)null : ApiRequest.Of(httpContext).Operation.Site;
        var expected = IfMatch.Required(httpContext.Request);
        var document = await RequestBody.ObjectAsync(httpContext.Request);

        await service.WriteAsync(descriptor, site, document.GetRawText(), expected, httpContext.RequestAborted);
        return Setting(httpContext, descriptor, site, await service.ResolveRedactedAsync(descriptor, site, httpContext.RequestAborted));
    }

    private static IResult Setting(HttpContext httpContext, SettingsDescriptor descriptor, SiteId? site, RedactedSetting setting)
    {
        IfMatch.SetETag(httpContext.Response, setting.Version);
        return AdminJson.Ok(AdminJson.Setting(descriptor, site?.Value, setting));
    }
}
