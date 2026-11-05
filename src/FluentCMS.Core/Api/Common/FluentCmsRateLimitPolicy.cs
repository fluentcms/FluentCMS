using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace FluentCMS.Core.Api.Common;

internal static class RateLimitPolicyNames
{
    public const string Delivery = "delivery";
    public const string Management = "management";
    public const string Auth = "auth";
}

// One named policy of the ASP.NET Core rate limiter. Responses carry the IETF draft fields RateLimit-Limit,
// RateLimit-Remaining and RateLimit-Reset (seconds), and a refused request also Retry-After.
internal abstract class FluentCmsRateLimitPolicy(int defaultLimit) : IRateLimiterPolicy<string>
{
    private readonly ConcurrentDictionary<string, WindowLimiter> _limiters = new();
    private readonly object _stateKey = new();

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => RejectAsync;

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        var (caller, limit) = Partition(httpContext);
        var key = $"{caller}|{limit}";

        if (!httpContext.Items.ContainsKey(_stateKey))
        {
            httpContext.Items[_stateKey] = key;
            httpContext.Response.OnStarting(static state =>
            {
                var (policy, context, key) = ((FluentCmsRateLimitPolicy, HttpContext, string))state;
                if (policy._limiters.TryGetValue(key, out var limiter))
                {
                    var headers = context.Response.Headers;
                    headers["RateLimit-Limit"] = limiter.Limit.ToString(CultureInfo.InvariantCulture);
                    headers["RateLimit-Remaining"] = limiter.Remaining.ToString(CultureInfo.InvariantCulture);
                    headers["RateLimit-Reset"] = limiter.SecondsToReset.ToString(CultureInfo.InvariantCulture);
                }

                return Task.CompletedTask;
            }, (this, httpContext, key));
        }

        return RateLimitPartition.Get(key, partitionKey =>
        {
            var limiter = new WindowLimiter(partitionKey, limit, _limiters);
            _limiters[partitionKey] = limiter;
            return limiter;
        });
    }

    protected int DefaultLimit => defaultLimit;

    protected abstract (string Caller, int Limit) Partition(HttpContext context);

    protected static string Address(HttpContext context) =>
        $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

    private async ValueTask RejectAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;

        int seconds;
        if (http.Items.TryGetValue(_stateKey, out var key) && _limiters.TryGetValue((string)key!, out var limiter))
        {
            seconds = limiter.SecondsToReset;
        }
        else if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        }
        else
        {
            seconds = (int)WindowLimiter.Window.TotalSeconds;
        }

        http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        await ProblemWriter.WriteAsync(
            http,
            ProblemMapper.Known(LimitErrorCodes.RateLimited, $"Too many requests; try again in {seconds} seconds."),
            cancellationToken: cancellationToken);
    }
}

internal sealed class DeliveryRateLimitPolicy(RateLimitOptions options) : FluentCmsRateLimitPolicy(options.Delivery.PermitLimit)
{
    protected override (string Caller, int Limit) Partition(HttpContext context)
    {
        var user = context.User;
        if (user.FindFirstValue(CmsClaimTypes.Kind) == nameof(PrincipalKind.ApiKey)
            && user.FindFirstValue(CmsClaimTypes.Subject) is { Length: > 0 } subject)
        {
            var limit = int.TryParse(user.FindFirstValue(CmsClaimTypes.RateLimit), NumberStyles.None, CultureInfo.InvariantCulture, out var own) && own > 0
                ? own
                : DefaultLimit;
            return ($"key:{subject}", limit);
        }

        return (Address(context), DefaultLimit);
    }
}

internal sealed class ManagementRateLimitPolicy(RateLimitOptions options) : FluentCmsRateLimitPolicy(options.Management.PermitLimit)
{
    protected override (string Caller, int Limit) Partition(HttpContext context) =>
        context.User.FindFirstValue(CmsClaimTypes.Subject) is { Length: > 0 } subject
            ? ($"sub:{subject}", DefaultLimit)
            : (Address(context), DefaultLimit);
}

internal sealed class AuthRateLimitPolicy(RateLimitOptions options) : FluentCmsRateLimitPolicy(options.Auth.PermitLimit)
{
    protected override (string Caller, int Limit) Partition(HttpContext context) => (Address(context), DefaultLimit);
}
