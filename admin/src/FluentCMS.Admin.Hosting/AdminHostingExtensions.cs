using System.Text;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity;
using FluentCMS.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticAssets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FluentCMS.Admin.Hosting;

public static class AdminHostingExtensions
{
    private const int BeforeStaticAssets = -1_000;

    private const string Surface = "admin";

    // The admin of admin spec 6.1, with FluentCms:Admin from configuration winning over what configure sets. While
    // FluentCms:Admin:Enabled is true, its default, it registers the admin's client under cms.Clients.AdminClientId, unless the
    // host or FluentCms:Clients lists one there, which then wins whole and which the startup report names when the admin cannot
    // sign in with it. While it is false and the options list no client, it deletes
    // the client it registered before; while they list any, the host deletes it, as any client they no longer list.
    public static FluentCmsBuilder AddAdmin(this FluentCmsBuilder cms, Action<AdminOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(cms);

        if (cms.Services.Any(service => service.ServiceType == typeof(AdminSite)))
        {
            return cms;
        }

        var options = new AdminOptions();
        configure?.Invoke(options);
        cms.Services.AddFluentCmsOptions(AdminOptions.Section, options, admin => admin.Validate());
        cms.Services.AddSingleton<AdminClientRegistration>();
        cms.Services.AddSingleton<IPostConfigureOptions<ClientOptions>>(services => services.GetRequiredService<AdminClientRegistration>());
        cms.Services.AddSingleton<IInsecureDefaultSource, AdminClientReport>();
        cms.Services.AddHostedService<AdminClientRemoval>();
        cms.Services.AddSingleton<AdminSite>();
        return cms;
    }

    // Serves the admin at /admin, under FluentCms:ReverseProxy:PathBase: the host's static web assets, which are the admin
    // app's when the host references its project and the package's first-party build otherwise, through MapStaticAssets (fingerprinted files cached for good, compressed
    // copies by Accept-Encoding); index.html with its <base href> set, for /admin/ and every client route; and config.json
    // built from FluentCms:Admin. index.html and config.json are never cached. With FluentCms:Admin:Enabled false nothing is
    // mapped, and /admin is a 404 like any unknown path. staticAssetsManifestPath is that of MapStaticAssets.
    public static IEndpointConventionBuilder MapFluentCmsAdmin(this IEndpointRouteBuilder endpoints, string? staticAssetsManifestPath = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var services = endpoints.ServiceProvider;
        if (services.GetService<AdminSite>() is not { } site)
        {
            throw FluentCmsConfigurationException.NotRegistered(
                "MapFluentCmsAdmin found no admin services. Call cms.AddAdmin() inside AddFluentCms(...).");
        }

        endpoints.EnsureFluentCmsPipeline(nameof(MapFluentCmsAdmin));

        if (!services.GetRequiredService<AdminOptions>().Enabled)
        {
            return new Conventions();
        }

        // At start, so that a host without the app, or an app whose index.html the host cannot set the base of, does not start.
        var manifest = Path.Combine(AppContext.BaseDirectory, staticAssetsManifestPath ?? $"{services.GetRequiredService<IWebHostEnvironment>().ApplicationName}.staticwebassets.endpoints.json");
        if (!File.Exists(manifest))
        {
            throw FluentCmsConfigurationException.NotRegistered(
                $"MapFluentCmsAdmin found no manifest of static web assets at {manifest}. A host that references FluentCMS.Admin.Hosting as a package gets the first-party build as its static web assets; one that references an admin app's project, such as FluentCMS.Admin.App, gets that app's files.");
        }

        site.LoadIndex();

        var admin = endpoints.MapGroup(AdminSite.Path).WithFluentCmsSurface(Surface);

        // The app's index.html and its sample config.json are answered as the endpoints below answer them, also where the
        // content negotiation of MapStaticAssets picks a compressed copy; their fingerprinted and compressed paths are not.
        // The app's other files read nothing of the instance, so they count under the static files limit, and the documents,
        // the endpoints below and the fallback under the coarse one.
        Func<HttpContext, string> policy = site.ContentSecurityPolicy;
        string[] methods = [HttpMethods.Get, HttpMethods.Head];
        var assets = admin.MapStaticAssets(manifest);
        assets.Add(endpoint =>
        {
            if (!endpoint.Metadata.OfType<StaticAssetDescriptor>().Any(asset => IsDocument(asset.AssetPath)))
            {
                endpoint.WithFluentCmsStaticFiles();
                return;
            }

            var route = ((RouteEndpointBuilder)endpoint).RoutePattern.RawText ?? "";
            if (route.EndsWith("/" + AdminSite.ConfigurationFile, StringComparison.Ordinal) || route == AdminSite.ConfigurationFile)
            {
                endpoint.RequestDelegate = context => ConfigurationAsync(context, site);
            }
            else if (route.EndsWith("/" + AdminSite.IndexFile, StringComparison.Ordinal) || route == AdminSite.IndexFile)
            {
                endpoint.RequestDelegate = context => PageAsync(context, site);
                endpoint.WithContentSecurityPolicy(policy);
            }
            else
            {
                endpoint.RequestDelegate = NotFound;
            }
        });

        var root = admin.MapMethods("/", methods, (HttpContext context) => PageAsync(context, site));
        var index = admin.MapMethods("/" + AdminSite.IndexFile, methods, (HttpContext context) => PageAsync(context, site));
        var configuration = admin.MapMethods("/" + AdminSite.ConfigurationFile, methods, (HttpContext context) => ConfigurationAsync(context, site));
        var routes = admin.MapFallback("{**path:nonfile}", (HttpContext context, string path) =>
            path.StartsWith("_framework/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("_content/", StringComparison.OrdinalIgnoreCase)
                ? NotFound(context)
                : PageAsync(context, site))
            .WithMetadata(new HttpMethodMetadata(methods));

        // Before the app's own copies of the same paths, whose endpoints MapStaticAssets gives the order -100.
        foreach (var builder in new[] { root, index, configuration })
        {
            builder.Add(endpoint => ((RouteEndpointBuilder)endpoint).Order = BeforeStaticAssets);
        }

        foreach (var builder in new[] { root, index, routes })
        {
            builder.WithContentSecurityPolicy(policy);
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

    private static async Task PageAsync(HttpContext context, AdminSite site)
    {
        // The app's router takes only addresses under its base, which is written as configured: /admin, and /ADMIN/ or a path
        // base in another case, go to the address the base names. The rest is built from the decoded path with each segment
        // escaped again, so that no %2e%2e or // a request carries escaped can lead the browser out of the base, and a dot
        // segment is refused.
        var request = context.Request;
        var path = request.Path.Value ?? "";
        if (!string.Equals(request.PathBase.Value ?? "", site.PathBase, StringComparison.Ordinal) || !path.StartsWith(AdminSite.Path + "/", StringComparison.Ordinal))
        {
            var segments = path.Length > AdminSite.Path.Length ? path[(AdminSite.Path.Length + 1)..].Split('/') : [];
            if (segments.Any(segment => segment is "." or ".."))
            {
                await NotFound(context);
                return;
            }

            context.Response.Redirect(site.BaseHref + string.Join('/', segments.Select(Uri.EscapeDataString)) + request.QueryString.ToUriComponent());
            return;
        }
        var html = await site.IndexAsync(context.RequestAborted);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        // Preview is a frame, which this leaves alone; nothing of the admin needs a window of another origin it opened (spec 6.1).
        context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
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
