using FluentCMS.Core.Api.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.DependencyInjection;

// For a package that maps endpoints of its own under a FluentCMS host, outside the groups MapFluentCms creates, as
// FluentCMS.Admin.Hosting maps /admin: what makes them a surface of FluentCMS, and the host's refusals at start.
public static class FluentCmsSurfaceExtensions
{
    // Core's surfaces, which the OpenAPI document, the request counter and the middleware tell apart by name.
    private static readonly string[] CoreSurfaces = ["delivery", "management", "members", "connect", "account", "health", "docs"];

    // Makes the endpoints FluentCMS endpoints of the surface name: UseFluentCms gives them its security headers, no-store
    // unless a response sets Cache-Control, the hold to the path base, the coarse limit before authentication, the request
    // limits and the FluentCMS authentication, and fluentcms.requests counts them under that name.
    public static TBuilder WithFluentCmsSurface<TBuilder>(this TBuilder builder, string name)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (CoreSurfaces.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"'{name}' is the name of a surface of core; give the package's surface a name of its own, such as its path.", nameof(name));
        }

        return builder.WithMetadata(FluentCmsEndpointMetadata.Instance, new FluentCmsSurfaceMetadata(name));
    }

    // The Content-Security-Policy of the responses of a surface's endpoints, made per request, for a page whose policy names
    // a nonce or an origin. A policy the response set itself stays.
    public static TBuilder WithContentSecurityPolicy<TBuilder>(this TBuilder builder, Func<HttpContext, string> policy)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(policy);
        return builder.WithMetadata(new ContentSecurityPolicyMetadata(policy));
    }

    // The same, for one endpoint of a convention that tells the endpoints of a mapping apart, as one of MapStaticAssets does.
    public static EndpointBuilder WithContentSecurityPolicy(this EndpointBuilder endpoint, Func<HttpContext, string> policy)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(policy);
        endpoint.Metadata.Add(new ContentSecurityPolicyMetadata(policy));
        return endpoint;
    }

    // Marks endpoints of a surface that serve its files as they are, such as fingerprinted scripts, which read nothing of the
    // instance: they count under FluentCms:RateLimits:StaticFiles instead of the coarse limit, and no key sent to them is
    // looked up. Leave it off a page, a configuration and a fallback, which count under the coarse limit.
    public static TBuilder WithFluentCmsStaticFiles<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(FluentCmsStaticFileMetadata.Instance);
    }

    public static EndpointBuilder WithFluentCmsStaticFiles(this EndpointBuilder endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        endpoint.Metadata.Add(FluentCmsStaticFileMetadata.Instance);
        return endpoint;
    }

    // Stops a mapping before UseFluentCms with FCMS1104, since nothing would read the surface's metadata; method is the
    // package's mapping method, which the message names, such as MapFluentCmsAdmin.
    public static void EnsureFluentCmsPipeline(this IEndpointRouteBuilder endpoints, string method)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        if (endpoints.ServiceProvider.GetService<ApiPipelineState>()?.Configured != true)
        {
            throw new FluentCmsConfigurationException(
                HostErrorCodes.PipelineMissing,
                $"{method} found no FluentCMS pipeline. Call app.UseFluentCms() before app.{method}().");
        }
    }

    // Binds FluentCms:{section} onto instance, which may hold what the host set in code, as core binds its own options: a key
    // the configuration sets wins, and the options are checked when the host starts, by their data annotations and by
    // validate, which throws FluentCmsConfigurationException.InvalidOption with a message that starts with the property. A
    // failure stops the host with FCMS1101 and the key. The options resolve as IOptions<T> and as T. The section is under
    // FluentCms, so a section that names FluentCms again is refused.
    public static IServiceCollection AddFluentCmsOptions<T>(this IServiceCollection services, string section, T instance, Action<T>? validate = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(section);
        ArgumentNullException.ThrowIfNull(instance);
        if (section.Equals(FluentCmsOptions.Section, StringComparison.OrdinalIgnoreCase) || section.StartsWith(FluentCmsOptions.Section + ":", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"'{section}' names the root section {FluentCmsOptions.Section}, which the section is under already; pass 'Reports' for {FluentCmsOptions.Section}:Reports.", nameof(section));
        }

        FluentCmsOptions.AddFluentCmsOptions(services, section, HostErrorCodes.InvalidOption, instance, validate);
        return services;
    }
}
