using FluentCMS.Core.DependencyInjection;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Core.Api.Common;

// The headers are set when the response starts, so they survive the exception handler clearing the response. A header an
// endpoint set itself stays, and frame-ancestors is added to the policy of an endpoint that has one without it. Only
// Delivery responses are meant for shared caches, so any other FluentCMS response that says nothing of caching is no-store:
// a key's secret, recovery codes, /me and the diagnostics are never kept by a browser or a proxy.
internal sealed class SecurityHeadersMiddleware(RequestDelegate next, SecurityOptions options)
{
    private const string FrameAncestors = "frame-ancestors";

    public Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<FluentCmsEndpointMetadata>() is not null)
        {
            var page = new Page(
                endpoint.Metadata.GetMetadata<HostedPageMetadata>() is not null,
                endpoint.Metadata.GetMetadata<ContentSecurityPolicyMetadata>(),
                Cached: FluentCmsSurfaceMetadata.IsDelivery(endpoint));
            context.Response.OnStarting(static state =>
            {
                var (context, options, page) = ((HttpContext, SecurityOptions, Page))state;
                var headers = context.Response.Headers;
                if (!headers.ContainsKey("X-Content-Type-Options"))
                {
                    headers.XContentTypeOptions = "nosniff";
                }

                if (!headers.ContainsKey("Referrer-Policy"))
                {
                    headers["Referrer-Policy"] = options.ReferrerPolicy;
                }

                if (!page.Cached && !headers.ContainsKey("Cache-Control"))
                {
                    headers.CacheControl = "no-store";
                }

                if (page.Policy is not null && headers.ContentSecurityPolicy.Count == 0)
                {
                    headers.ContentSecurityPolicy = page.Policy.For(context);
                }

                if (page.Hosted)
                {
                    var directive = $"{FrameAncestors} {options.FrameAncestors}";
                    var existing = headers.ContentSecurityPolicy.ToString();
                    if (existing.Length == 0)
                    {
                        headers.ContentSecurityPolicy = directive;
                    }
                    else if (!HasFrameAncestors(existing))
                    {
                        headers.ContentSecurityPolicy = $"{existing.TrimEnd().TrimEnd(';')}; {directive}";
                    }
                }

                return Task.CompletedTask;
            }, (context, options, page));
        }

        return next(context);
    }

    // Policies are separated by commas and directives by semicolons; one with the directive in any of them is left as it is.
    private static bool HasFrameAncestors(string policy) =>
        policy.Split(',', ';').Any(directive => directive.AsSpan().TrimStart().StartsWith(FrameAncestors, StringComparison.OrdinalIgnoreCase));

    private sealed record Page(bool Hosted, ContentSecurityPolicyMetadata? Policy, bool Cached);
}
