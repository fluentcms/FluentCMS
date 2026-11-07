using FluentCMS.Core.DependencyInjection;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Core.Api.Common;

// UsePathBase leaves a request that does not carry the prefix as it is, so without this the routes would answer both under
// the path base and at the root. A request is held to the path base when routing found a FluentCMS endpoint, or when its
// path is under a prefix of FluentCmsEndpointGroups, which is how the authorization server's own paths are covered. It runs
// after the security headers and CORS, and answers as problem+json. Routes a host maps itself are its own concern.
internal sealed class PathBaseGateMiddleware(RequestDelegate next, ReverseProxyOptions options)
{
    private readonly PathString _pathBase = new(options.PathBase);

    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.PathBase != _pathBase && IsFluentCms(context))
        {
            return ProblemWriter.WriteAsync(
                context,
                ProblemMapper.Known(ApiErrorCodes.NotFound, $"FluentCMS is served under {_pathBase}; this path is not."),
                cancellationToken: context.RequestAborted);
        }

        return next(context);
    }

    // The document and the Scalar UI are where the host maps them.
    private static bool IsFluentCms(HttpContext context) =>
        context.GetEndpoint() is { } endpoint && endpoint.Metadata.GetMetadata<FluentCmsEndpointMetadata>() is not null
            ? !FluentCmsSurfaceMetadata.IsDocs(endpoint)
            : FluentCmsEndpointGroups.Prefixes.Any(entry => context.Request.Path.StartsWithSegments(entry.Prefix));
}
