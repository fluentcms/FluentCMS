using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FluentCMS.Core.DependencyInjection;

internal static class ReverseProxyPipeline
{
    public static void AddReverseProxyHttpsPolicy(this IServiceCollection services)
    {
        services.AddOptions<HstsOptions>().Configure<ReverseProxyOptions>((hsts, proxy) =>
        {
            hsts.MaxAge = proxy.Hsts.MaxAge;
            hsts.IncludeSubDomains = proxy.Hsts.IncludeSubDomains;
            hsts.Preload = proxy.Hsts.Preload;
        });
        services.AddOptions<HttpsRedirectionOptions>().Configure<ReverseProxyOptions>((redirection, proxy) =>
        {
            // A port that is not set stays the framework's to find, from HTTPS_PORT or the server's https address.
            if (proxy.HttpsRedirection.Port is { } port)
            {
                redirection.HttpsPort = port;
            }
        });
    }

    public static bool HstsEnabled(ReverseProxyOptions options, IHostEnvironment? environment) =>
        options.Hsts.Enabled ?? environment?.IsDevelopment() != true;

    // The order is the point: forwarded headers first, so that what comes after, from the redirect to the limit per client
    // address, sees the scheme and address the proxy forwarded; then the path base, then HSTS and the redirect.
    public static void Use(IApplicationBuilder app, ReverseProxyOptions options)
    {
        if (options.ForwardedHeaders.Enabled)
        {
            app.UseForwardedHeaders(Forwarded(options.ForwardedHeaders, app.ApplicationServices.GetService<IOptions<HostFilteringOptions>>()?.Value));
        }

        if (!string.IsNullOrEmpty(options.PathBase))
        {
            app.UsePathBase(options.PathBase);
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

    private static ForwardedHeadersOptions Forwarded(ProxyForwardedHeadersOptions options, HostFilteringOptions? hostFiltering)
    {
        var forwarded = new ForwardedHeadersOptions
        {
            ForwardedHeaders = (options.ForwardFor ? ForwardedHeaders.XForwardedFor : ForwardedHeaders.None)
                | (options.ForwardProto ? ForwardedHeaders.XForwardedProto : ForwardedHeaders.None)
                | (options.ForwardHost ? ForwardedHeaders.XForwardedHost : ForwardedHeaders.None),
            ForwardLimit = options.ForwardLimit,
        };

        // Host filtering runs before this middleware and sees the proxy's Host, so a forwarded host is held to AllowedHosts
        // here; otherwise a forged X-Forwarded-Host would get past it.
        if (options.ForwardHost && hostFiltering?.AllowedHosts is { Count: > 0 } allowedHosts)
        {
            forwarded.AllowedHosts = [.. allowedHosts];
        }

        if (options.KnownProxies.Count == 0 && options.KnownNetworks.Count == 0)
        {
            return forwarded;
        }

        // Listing a proxy or a network replaces the loopback default, which is how the framework behaves when a host lists them.
        forwarded.KnownProxies.Clear();
        forwarded.KnownIPNetworks.Clear();
        foreach (var proxy in options.KnownProxies)
        {
            forwarded.KnownProxies.Add(ProxyAddress.TryParse(proxy, out var address) ? address : throw new InvalidOperationException());
        }

        foreach (var network in options.KnownNetworks)
        {
            forwarded.KnownIPNetworks.Add(ProxyAddress.TryParseNetwork(network, out var parsed) ? parsed : throw new InvalidOperationException());
        }

        return forwarded;
    }
}
