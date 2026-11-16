using System.Text;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticAssets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FluentCMS.Admin.Hosting;

public static class AdminHostingExtensions
{
    private const int BeforeStaticAssets = -1_000;

    private static readonly FluentCmsSurfaceMetadata Surface = new("admin");

    // The admin of admin spec 6.1, with FluentCms:Admin from configuration winning over what configure sets. While
    // FluentCms:Admin:Enabled is true it registers the admin's client under cms.Clients.AdminClientId, unless the host or
    // FluentCms:Clients lists one there, which then wins whole.
    public static FluentCmsBuilder AddAdmin(this FluentCmsBuilder cms, Action<AdminOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(cms);

        if (cms.Services.Any(service => service.ServiceType == typeof(AdminSite)))
        {
            return cms;
        }

        var options = new AdminOptions();
        configure?.Invoke(options);
        cms.Services.AddFluentCmsOptions(AdminOptions.Section, HostErrorCodes.InvalidOption, options, admin => admin.Validate());
        cms.Services.AddSingleton<IPostConfigureOptions<ClientOptions>, AdminClientRegistration>();
        cms.Services.AddSingleton<AdminSite>();
        return cms;
    }

    // Serves the admin at /admin, under FluentCms:ReverseProxy:PathBase: the host's static web assets, which are the admin
    // app's when the host references its project, through MapStaticAssets (fingerprinted files cached for good, compressed
    // copies by Accept-Encoding); index.html with its <base href> set, for /admin/ and every client route; and config.json
    // built from FluentCms:Admin. index.html and config.json are never cached. With FluentCms:Admin:Enabled false nothing is
    // mapped, and /admin is a 404 like any unknown path. staticAssetsManifestPath is that of MapStaticAssets.
    public static IEndpointConventionBuilder MapFluentCmsAdmin(this IEndpointRouteBuilder endpoints, string? staticAssetsManifestPath = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var services = endpoints.ServiceProvider;
        if (services.GetService<AdminSite>() is not { } site)
        {
            throw new FluentCmsConfigurationException(
                HostErrorCodes.NotRegistered,
                "MapFluentCmsAdmin found no admin services. Call cms.AddAdmin() inside AddFluentCms(...).");
        }

        if (services.GetService<ApiPipelineState>()?.Configured != true)
        {
            throw new FluentCmsConfigurationException(
                HostErrorCodes.PipelineMissing,
                "MapFluentCmsAdmin found no FluentCMS pipeline. Call app.UseFluentCms() before app.MapFluentCmsAdmin().");
        }

        if (!services.GetRequiredService<AdminOptions>().Enabled)
        {
            return new Conventions();
        }

        var admin = endpoints.MapGroup(AdminSite.Path);
        admin.WithMetadata(FluentCmsEndpointMetadata.Instance, Surface);

        // The app's index.html and its sample config.json are answered as the endpoints below answer them, also where the
        // content negotiation of MapStaticAssets picks a compressed copy; their fingerprinted and compressed paths are not.
        var policy = new ContentSecurityPolicyMetadata(site.ContentSecurityPolicy);
        string[] methods = [HttpMethods.Get, HttpMethods.Head];
        var assets = admin.MapStaticAssets(staticAssetsManifestPath);
        assets.Add(endpoint =>
        {
            if (!endpoint.Metadata.OfType<StaticAssetDescriptor>().Any(asset => IsDocument(asset.AssetPath)))
            {
                return;
            }

            var route = ((RouteEndpointBuilder)endpoint).RoutePattern.RawText ?? "";
            if (route.EndsWith("/" + AdminSite.ConfigurationFile, StringComparison.Ordinal) || route == AdminSite.ConfigurationFile)
            {
                endpoint.RequestDelegate = context => ConfigurationAsync(context, site);
            }
            else if (route.EndsWith("/" + AdminSite.IndexFile, StringComparison.Ordinal) || route == AdminSite.IndexFile)
            {
                endpoint.RequestDelegate = context => PageAsync(context, site, redirect: false);
                endpoint.Metadata.Add(policy);
            }
            else
            {
                endpoint.RequestDelegate = NotFound;
            }
        });

        var root = admin.MapMethods("/", methods, (HttpContext context) => PageAsync(context, site, redirect: true));
        var index = admin.MapMethods("/" + AdminSite.IndexFile, methods, (HttpContext context) => PageAsync(context, site, redirect: false));
        var configuration = admin.MapMethods("/" + AdminSite.ConfigurationFile, methods, (HttpContext context) => ConfigurationAsync(context, site));
        var routes = admin.MapFallback("{**path:nonfile}", (HttpContext context, string path) =>
            path.StartsWith("_framework/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("_content/", StringComparison.OrdinalIgnoreCase)
                ? NotFound(context)
                : PageAsync(context, site, redirect: false))
            .WithMetadata(new HttpMethodMetadata(methods));

        // Before the app's own copies of the same paths, whose endpoints MapStaticAssets gives the order -100.
        foreach (var builder in new[] { root, index, configuration })
        {
            builder.Add(endpoint => ((RouteEndpointBuilder)endpoint).Order = BeforeStaticAssets);
        }

        foreach (var builder in new[] { root, index, routes })
        {
            builder.WithMetadata(policy);
        }

        return new Conventions(assets, root, index, configuration, routes);
    }

    private static bool IsDocument(string assetPath)
    {
        var path = assetPath.EndsWith(".gz", StringComparison.Ordinal) ? assetPath[..^3]
            : assetPath.EndsWith(".br", StringComparison.Ordinal) ? assetPath[..^3]
            : assetPath;
        return path is AdminSite.IndexFile or AdminSite.ConfigurationFile;
    }

    private static Task NotFound(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }

    private static async Task PageAsync(HttpContext context, AdminSite site, bool redirect)
    {
        // At /admin the app's base, /admin/, would not contain the page's address.
        if (redirect && !context.Request.Path.Value!.EndsWith('/'))
        {
            context.Response.Redirect(context.Request.PathBase.Add(AdminSite.Path + "/").ToUriComponent() + context.Request.QueryString.ToUriComponent());
            return;
        }

        var html = await site.IndexAsync(context.RequestAborted);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsync(html, Encoding.UTF8, context.RequestAborted);
    }

    private static async Task ConfigurationAsync(HttpContext context, AdminSite site)
    {
        var json = site.Configuration(context.Request);
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        context.Response.ContentLength = json.Length;
        await context.Response.Body.WriteAsync(json, context.RequestAborted);
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
