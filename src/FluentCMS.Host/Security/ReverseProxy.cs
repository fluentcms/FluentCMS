using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace FluentCMS.Host.Security;

// The proxies whose X-Forwarded-For the host believes. Without any, the header is ignored, so a client cannot choose the address that
// rate limits count it by; behind a proxy, every client would otherwise share the proxy's address (SEC-07).
public sealed class ReverseProxyOptions
{
    public const string SectionName = "ReverseProxy";

    // Addresses such as 10.0.0.5.
    public List<string> KnownProxies { get; set; } = [];

    // Networks in CIDR notation such as 10.0.0.0/8.
    public List<string> KnownNetworks { get; set; } = [];

    public bool IsConfigured => KnownProxies.Count > 0 || KnownNetworks.Count > 0;
}

internal sealed class ReverseProxyOptionsValidation : IValidateOptions<ReverseProxyOptions>
{
    public ValidateOptionsResult Validate(string? name, ReverseProxyOptions options)
    {
        var failures = options.KnownProxies
            .Where(proxy => !IPAddress.TryParse(proxy, out _))
            .Select(proxy => $"ReverseProxy__KnownProxies has '{proxy}', which is not an IP address such as 10.0.0.5.")
            .Concat(options.KnownNetworks
                .Where(network => !System.Net.IPNetwork.TryParse(network, out _))
                .Select(network => $"ReverseProxy__KnownNetworks has '{network}', which is not a network such as 10.0.0.0/8."))
            .ToList();

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

internal static class ReverseProxy
{
    public static IServiceCollection AddReverseProxy(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ReverseProxyOptions>().Bind(configuration.GetSection(ReverseProxyOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<ReverseProxyOptions>, ReverseProxyOptionsValidation>();
        return services;
    }

    // Before the rate limiters, so they count the client the trusted proxy names. Only the nearest proxy's entry is taken.
    public static IApplicationBuilder UseReverseProxy(this IApplicationBuilder app)
    {
        var proxy = app.ApplicationServices.GetRequiredService<IOptions<ReverseProxyOptions>>().Value;
        if (!proxy.IsConfigured)
        {
            return app;
        }

        var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
        forwarded.KnownProxies.Clear();
        forwarded.KnownIPNetworks.Clear();
        foreach (var address in proxy.KnownProxies)
        {
            forwarded.KnownProxies.Add(IPAddress.Parse(address));
        }

        foreach (var network in proxy.KnownNetworks)
        {
            forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        }

        return app.UseForwardedHeaders(forwarded);
    }
}
