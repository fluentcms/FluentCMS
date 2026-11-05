using System.Threading.RateLimiting;
using FluentCMS.Core.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace FluentCMS.Core.Api.Common;

// Limits a client address on the FluentCMS routes before anything authenticates it. The rate limiter of the endpoints runs
// after authentication, which is too late for a flood of API keys, each looked up in the database, or of requests to the
// token endpoint, which the authorization server answers while authenticating. It goes by path, since that endpoint is not
// a routed one.
internal sealed class PreAuthRateLimiter(PreAuthRateLimitPolicy policy) : IDisposable
{
    private static readonly PathString[] Prefixes =
    [
        .. FluentCmsEndpointGroups.Prefixes.Where(entry => entry.Group != FluentCmsEndpointGroup.Health).Select(entry => new PathString(entry.Prefix)),
    ];

    private readonly PartitionedRateLimiter<HttpContext> _limiter = PartitionedRateLimiter.Create<HttpContext, string>(policy.GetPartition);

    public static bool Applies(PathString path) => Prefixes.Any(prefix => path.StartsWithSegments(prefix));

    public async Task<bool> TryAcquireAsync(HttpContext context)
    {
        using var lease = await _limiter.AcquireAsync(context, permitCount: 1, context.RequestAborted);
        if (lease.IsAcquired)
        {
            return true;
        }

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await policy.OnRejected!(new OnRejectedContext { HttpContext = context, Lease = lease }, context.RequestAborted);
        return false;
    }

    public void Dispose() => _limiter.Dispose();
}

internal sealed class PreAuthRateLimitMiddleware(RequestDelegate next, PreAuthRateLimiter limiter)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!PreAuthRateLimiter.Applies(context.Request.Path) || await limiter.TryAcquireAsync(context))
        {
            await next(context);
        }
    }
}
