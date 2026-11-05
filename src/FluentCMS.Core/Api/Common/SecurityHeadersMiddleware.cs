using FluentCMS.Core.DependencyInjection;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Core.Api.Common;

// The headers are set when the response starts, so they survive the exception handler clearing the response.
internal sealed class SecurityHeadersMiddleware(RequestDelegate next, SecurityOptions options)
{
    public Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<FluentCmsEndpointMetadata>() is not null)
        {
            var hostedPage = endpoint.Metadata.GetMetadata<HostedPageMetadata>() is not null;
            context.Response.OnStarting(static state =>
            {
                var (headers, options, hostedPage) = ((IHeaderDictionary, SecurityOptions, bool))state;
                headers.XContentTypeOptions = "nosniff";
                headers["Referrer-Policy"] = options.ReferrerPolicy;
                if (hostedPage)
                {
                    headers.ContentSecurityPolicy = $"frame-ancestors {options.FrameAncestors}";
                }

                return Task.CompletedTask;
            }, (context.Response.Headers, options, hostedPage));
        }

        return next(context);
    }
}
