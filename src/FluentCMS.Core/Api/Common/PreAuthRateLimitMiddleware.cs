using System.Threading.RateLimiting;
using FluentCMS.Core.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace FluentCMS.Core.Api.Common;

// Limits a client address on the FluentCMS routes before anything authenticates it. The rate limiter of the endpoints runs
// after authentication, which is too late for a flood of API keys, each looked up in the database, or of requests to the
// token endpoint, which the authorization server answers while authenticating. It goes by path, since that endpoint is not
// a routed one: every route but /health gets the coarse limit, and /connect and /account also the strict auth limit, which
// the endpoints of those groups then do not count a second time.
internal sealed class PreAuthRateLimiter : IDisposable
{
    private static readonly PathString[] Prefixes =
    [
        .. FluentCmsEndpointGroups.Prefixes.Where(entry => entry.Group != FluentCmsEndpointGroup.Health).Select(entry => new PathString(entry.Prefix)),
    ];

    private static readonly PathString[] AuthPrefixes =
    [
        .. FluentCmsEndpointGroups.Prefixes.Where(entry => entry.Group is FluentCmsEndpointGroup.Connect or FluentCmsEndpointGroup.Account).Select(entry => new PathString(entry.Prefix)),
    ];

    private readonly PreAuthRateLimitPolicy _coarsePolicy;
    private readonly AuthRateLimitPolicy _authPolicy;
    private readonly PartitionedRateLimiter<HttpContext> _coarse;
    private readonly PartitionedRateLimiter<HttpContext> _auth;

    public PreAuthRateLimiter(PreAuthRateLimitPolicy coarse, AuthRateLimitPolicy auth)
    {
        _coarsePolicy = coarse;
        _authPolicy = auth;
        _coarse = PartitionedRateLimiter.Create<HttpContext, string>(coarse.GetPartition);
        _auth = PartitionedRateLimiter.Create<HttpContext, string>(auth.GetPartition);
    }

    public static bool Applies(PathString path) => Prefixes.Any(prefix => path.StartsWithSegments(prefix));

    public async Task<bool> TryAcquireAsync(HttpContext context)
    {
        if (!await TryAcquireAsync(context, _coarse, _coarsePolicy))
        {
            return false;
        }

        if (!AuthPrefixes.Any(prefix => context.Request.Path.StartsWithSegments(prefix)))
        {
            return true;
        }

        if (!await TryAcquireAsync(context, _auth, _authPolicy))
        {
            return false;
        }

        _authPolicy.MarkCounted(context);
        return true;
    }

    public void Dispose()
    {
        _coarse.Dispose();
        _auth.Dispose();
    }

    private static async Task<bool> TryAcquireAsync(HttpContext context, PartitionedRateLimiter<HttpContext> limiter, FluentCmsRateLimitPolicy policy)
    {
        using var lease = await limiter.AcquireAsync(context, permitCount: 1, context.RequestAborted);
        if (lease.IsAcquired)
        {
            return true;
        }

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await policy.OnRejected!(new OnRejectedContext { HttpContext = context, Lease = lease }, context.RequestAborted);
        return false;
    }
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
