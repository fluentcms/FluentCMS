using System.Threading.RateLimiting;
using FluentCMS.Core.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace FluentCMS.Core.Api.Common;

internal enum PreAuthLimit
{
    None,
    Coarse,
    Health,
}

// Limits a client address on the FluentCMS routes before anything authenticates it. The rate limiter of the endpoints runs
// after authentication, which is too late for a flood of API keys, each looked up in the database, or of requests to the
// token endpoint, which the authorization server answers while authenticating. Routing has run, so a FluentCMS endpoint,
// a plugin's included, is known by its metadata, and a path under a group of core gets the limit even when no endpoint
// answers it, as none answers the token endpoint. /connect, /account and /api/members/v1 also get the strict auth limit,
// which the endpoints of those groups then do not count a second time. /health counts in a partition of its own, so that probes
// and API traffic from one address do not use up each other's allowance.
internal sealed class PreAuthRateLimiter : IDisposable
{
    private static readonly PathString HealthPrefix = new(FluentCmsEndpointGroups.Prefixes.Single(entry => entry.Group == FluentCmsEndpointGroup.Health).Prefix);

    private static readonly PathString[] Prefixes =
    [
        .. FluentCmsEndpointGroups.Prefixes.Where(entry => entry.Group != FluentCmsEndpointGroup.Health).Select(entry => new PathString(entry.Prefix)),
    ];

    private static readonly PathString[] AuthPrefixes =
    [
        .. FluentCmsEndpointGroups.Prefixes.Where(entry => entry.Group is FluentCmsEndpointGroup.Connect or FluentCmsEndpointGroup.Account or FluentCmsEndpointGroup.Members).Select(entry => new PathString(entry.Prefix)),
    ];

    private readonly PreAuthRateLimitPolicy _coarsePolicy;
    private readonly HealthRateLimitPolicy _healthPolicy;
    private readonly AuthRateLimitPolicy _authPolicy;
    private readonly PartitionedRateLimiter<HttpContext> _coarse;
    private readonly PartitionedRateLimiter<HttpContext> _health;
    private readonly PartitionedRateLimiter<HttpContext> _auth;

    public PreAuthRateLimiter(PreAuthRateLimitPolicy coarse, HealthRateLimitPolicy health, AuthRateLimitPolicy auth)
    {
        _coarsePolicy = coarse;
        _healthPolicy = health;
        _authPolicy = auth;
        _coarse = PartitionedRateLimiter.Create<HttpContext, string>(coarse.GetPartition);
        _health = PartitionedRateLimiter.Create<HttpContext, string>(health.GetPartition);
        _auth = PartitionedRateLimiter.Create<HttpContext, string>(auth.GetPartition);
    }

    public static PreAuthLimit LimitOf(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<FluentCmsEndpointMetadata>() is not null)
        {
            return FluentCmsSurfaceMetadata.IsHealth(endpoint) ? PreAuthLimit.Health : PreAuthLimit.Coarse;
        }

        var path = context.Request.Path;
        if (path.StartsWithSegments(HealthPrefix))
        {
            return PreAuthLimit.Health;
        }

        return Prefixes.Any(prefix => path.StartsWithSegments(prefix)) ? PreAuthLimit.Coarse : PreAuthLimit.None;
    }

    public async Task<bool> TryAcquireAsync(HttpContext context, PreAuthLimit limit)
    {
        if (limit == PreAuthLimit.Health)
        {
            return await TryAcquireAsync(context, _health, _healthPolicy);
        }

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
        _health.Dispose();
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
        var limit = PreAuthRateLimiter.LimitOf(context);
        if (limit == PreAuthLimit.None || await limiter.TryAcquireAsync(context, limit))
        {
            await next(context);
        }
    }
}
