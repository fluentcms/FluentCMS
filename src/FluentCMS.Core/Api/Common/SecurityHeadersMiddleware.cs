using FluentCMS.Core.DependencyInjection;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Core.Api.Common;

// The headers are set when the response starts, so they survive the exception handler clearing the response. A header an
// endpoint set itself stays, and frame-ancestors is added to the policy of an endpoint that has one without it.
internal sealed class SecurityHeadersMiddleware(RequestDelegate next, SecurityOptions options)
{
    private const string FrameAncestors = "frame-ancestors";

    public Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<FluentCmsEndpointMetadata>() is not null)
        {
            var hostedPage = endpoint.Metadata.GetMetadata<HostedPageMetadata>() is not null;
            context.Response.OnStarting(static state =>
            {
                var (headers, options, hostedPage) = ((IHeaderDictionary, SecurityOptions, bool))state;
                if (!headers.ContainsKey("X-Content-Type-Options"))
                {
                    headers.XContentTypeOptions = "nosniff";
                }

                if (!headers.ContainsKey("Referrer-Policy"))
                {
                    headers["Referrer-Policy"] = options.ReferrerPolicy;
                }

                if (hostedPage)
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
            }, (context.Response.Headers, options, hostedPage));
        }

        return next(context);
    }

    // Policies are separated by commas and directives by semicolons; one with the directive in any of them is left as it is.
    private static bool HasFrameAncestors(string policy) =>
        policy.Split(',', ';').Any(directive => directive.AsSpan().TrimStart().StartsWith(FrameAncestors, StringComparison.OrdinalIgnoreCase));
}
