using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace FluentCMS.Core.Api.Common;

// Marks the endpoints MapFluentCms creates, so that the middleware of UseFluentCms leaves a host's own endpoints alone.
internal sealed class FluentCmsEndpointMetadata
{
    public static readonly FluentCmsEndpointMetadata Instance = new();
}

// Marks the hosted pages, whose responses also carry a Content-Security-Policy.
internal sealed class HostedPageMetadata
{
    public static readonly HostedPageMetadata Instance = new();
}

// The Content-Security-Policy of an endpoint's responses, made per request, for a page whose policy names a nonce or an
// origin. A policy the endpoint set itself stays.
internal sealed class ContentSecurityPolicyMetadata(Func<HttpContext, string> policy)
{
    public string For(HttpContext context) => policy(context);
}

internal static class HostedPageConventions
{
    // What the hosted pages get: the FluentCMS headers with frame-ancestors, and the strict auth rate limit. The /account
    // group applies it, and so does the mapping of the Razor Pages that are the pages themselves.
    public static TBuilder AsHostedPages<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(FluentCmsEndpointMetadata.Instance, HostedPageMetadata.Instance).RequireRateLimiting(RateLimitPolicyNames.Auth);
}
