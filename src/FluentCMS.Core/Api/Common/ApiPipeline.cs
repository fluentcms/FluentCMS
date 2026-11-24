using Microsoft.AspNetCore.Http;

namespace FluentCMS.Core.Api.Common;

// Set by UseFluentCms and checked by MapFluentCms, which would otherwise map endpoints whose CORS and rate limit metadata
// nothing reads, or that fail on every request.
internal sealed class ApiPipelineState
{
    public bool Configured { get; set; }
}

// Puts the principal of the FluentCms scheme where the rate limit policies and the output cache read it, as an ApiCaller
// read once. The probes, the docs and the static files a surface marks need no principal, so a key sent to /health,
// /openapi/v1.json, /scalar or such a file is never looked up, and the StaticFiles limit lets through no lookup the coarse
// limit would have held back.
internal sealed class ApiAuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<FluentCmsEndpointMetadata>() is not null && !FluentCmsSurfaceMetadata.IsHealth(endpoint) && !FluentCmsSurfaceMetadata.IsDocs(endpoint)
            && PreAuthRateLimiter.LimitOf(context) != PreAuthLimit.StaticFiles)
        {
            await ApiAuthentication.AuthenticateAsync(context);
            ApiCaller.Settle(context);
        }

        await next(context);
    }
}
