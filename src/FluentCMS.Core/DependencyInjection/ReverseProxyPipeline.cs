using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FluentCMS.Core.DependencyInjection;

internal static class ReverseProxyPipeline
{
    // Without a port the framework looks for one in the server's addresses, and behind a proxy there is none to find.
    private const int DefaultHttpsPort = 443;

    // HSTS and HTTPS redirection are the framework's middleware, which read their options from the container.
    public static void AddReverseProxyHttpsPolicy(this IServiceCollection services)
    {
        services.AddOptions<HstsOptions>().Configure<ReverseProxyOptions>((hsts, proxy) =>
        {
            hsts.MaxAge = proxy.Hsts.MaxAge;
            hsts.IncludeSubDomains = proxy.Hsts.IncludeSubDomains;
            hsts.Preload = proxy.Hsts.Preload;
        });
        services.AddOptions<HttpsRedirectionOptions>().Configure<ReverseProxyOptions>((redirection, proxy) =>
            redirection.HttpsPort = proxy.HttpsRedirection.Port ?? DefaultHttpsPort);
    }

    public static bool HstsEnabled(ReverseProxyOptions options, IHostEnvironment? environment) =>
        options.Hsts.Enabled ?? environment?.IsDevelopment() != true;

    // The order is the point: forwarded headers first, so that what comes after, from the redirect to the limit per client
    // address, sees the scheme and address the proxy forwarded; then the path base, then HSTS and the redirect.
    public static void Use(IApplicationBuilder app, ReverseProxyOptions options)
    {
        if (options.ForwardedHeaders.Enabled)
        {
            app.UseForwardedHeaders(Forwarded(options.ForwardedHeaders));
        }

        if (!string.IsNullOrEmpty(options.PathBase))
        {
            var pathBase = new PathString(options.PathBase);
            app.UsePathBase(pathBase);
            app.Use(RequirePathBase(pathBase));
        }

        if (HstsEnabled(options, app.ApplicationServices.GetService<IHostEnvironment>()))
        {
            app.UseHsts();
        }

        if (options.HttpsRedirection.Enabled)
        {
            app.UseHttpsRedirection();
        }
    }

    // UsePathBase leaves a request that does not carry the prefix as it is, so without this the routes would answer both
    // under the path base and at the root, and a probe or a link that forgot the prefix would work by accident.
    private static Func<HttpContext, RequestDelegate, Task> RequirePathBase(PathString pathBase) => (context, next) =>
    {
        if (context.Request.PathBase != pathBase && FluentCmsEndpointGroups.Prefixes.Any(entry => context.Request.Path.StartsWithSegments(entry.Prefix)))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        return next(context);
    };

    private static ForwardedHeadersOptions Forwarded(ProxyForwardedHeadersOptions options)
    {
        var forwarded = new ForwardedHeadersOptions
        {
            ForwardedHeaders = (options.ForwardFor ? ForwardedHeaders.XForwardedFor : ForwardedHeaders.None)
                | (options.ForwardProto ? ForwardedHeaders.XForwardedProto : ForwardedHeaders.None)
                | (options.ForwardHost ? ForwardedHeaders.XForwardedHost : ForwardedHeaders.None),
            ForwardLimit = options.ForwardLimit,
        };

        if (options.KnownProxies.Count == 0 && options.KnownNetworks.Count == 0)
        {
            return forwarded;
        }

        // Listing a proxy or a network replaces the loopback default, which is how the framework behaves when a host lists them.
        forwarded.KnownProxies.Clear();
        forwarded.KnownIPNetworks.Clear();
        foreach (var proxy in options.KnownProxies)
        {
            forwarded.KnownProxies.Add(IPAddress.Parse(proxy));
        }

        foreach (var network in options.KnownNetworks)
        {
            forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        }

        return forwarded;
    }
}
