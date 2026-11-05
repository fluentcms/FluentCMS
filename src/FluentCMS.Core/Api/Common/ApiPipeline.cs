using Microsoft.AspNetCore.Http;

namespace FluentCMS.Core.Api.Common;

// Set by UseFluentCms and checked by MapFluentCms, which would otherwise map endpoints whose CORS and rate limit metadata
// nothing reads, or that fail on every request.
internal sealed class ApiPipelineState
{
    public bool Configured { get; set; }
}

// Puts the principal of the FluentCms scheme where the rate limit policies read it.
internal sealed class ApiAuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<FluentCmsEndpointMetadata>() is not null)
        {
            await ApiAuthentication.AuthenticateAsync(context);
        }

        await next(context);
    }
}
