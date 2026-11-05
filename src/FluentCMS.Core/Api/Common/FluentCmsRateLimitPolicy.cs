using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
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

// One policy of the ASP.NET Core rate limiter. Responses carry the IETF draft fields RateLimit-Limit, RateLimit-Remaining
// and RateLimit-Reset (seconds), and a refused request also Retry-After.
internal abstract class FluentCmsRateLimitPolicy(int defaultLimit, TimeProvider time, bool reportsHeaders = true) : IRateLimiterPolicy<string>
{
    private readonly ConcurrentDictionary<string, WindowLimiter> _limiters = new();
    private readonly object _stateKey = new();

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => RejectAsync;

    protected int DefaultLimit => defaultLimit;

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        var (caller, limit) = Partition(httpContext);
        var key = $"{caller}|{limit}";

        if (!httpContext.Items.ContainsKey(_stateKey))
        {
            httpContext.Items[_stateKey] = key;
            if (reportsHeaders)
            {
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
        }

        return RateLimitPartition.Get(key, partitionKey =>
        {
            var limiter = new WindowLimiter(partitionKey, limit, time, _limiters);
            _limiters[partitionKey] = limiter;
            return limiter;
        });
    }

    protected abstract (string Caller, int Limit) Partition(HttpContext context);

    protected static string Address(HttpContext context) => $"ip:{ClientAddress.Partition(context.Connection.RemoteIpAddress)}";

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

internal sealed class DeliveryRateLimitPolicy(RateLimitOptions options, TimeProvider time) : FluentCmsRateLimitPolicy(options.Delivery.PermitLimit, time)
{
    protected override (string Caller, int Limit) Partition(HttpContext context)
    {
        var user = ApiAuthentication.Of(context);
        if (user.FindFirstValue(CmsClaimTypes.Kind) == nameof(PrincipalKind.ApiKey)
            && user.FindFirstValue(CmsClaimTypes.Subject) is { Length: > 0 } subject)
        {
            var limit = int.TryParse(user.FindFirstValue(CmsClaimTypes.RateLimit), NumberStyles.None, CultureInfo.InvariantCulture, out var own) && own > 0
                ? Math.Min(own, options.Delivery.MaxKeyLimit)
                : DefaultLimit;
            return ($"key:{subject}", limit);
        }

        return (Address(context), DefaultLimit);
    }
}

internal sealed class ManagementRateLimitPolicy(RateLimitOptions options, TimeProvider time) : FluentCmsRateLimitPolicy(options.Management.PermitLimit, time)
{
    protected override (string Caller, int Limit) Partition(HttpContext context) =>
        ApiAuthentication.Of(context).FindFirstValue(CmsClaimTypes.Subject) is { Length: > 0 } subject
            ? ($"sub:{subject}", DefaultLimit)
            : (Address(context), DefaultLimit);
}

internal sealed class AuthRateLimitPolicy(RateLimitOptions options, TimeProvider time) : FluentCmsRateLimitPolicy(options.Auth.PermitLimit, time)
{
    protected override (string Caller, int Limit) Partition(HttpContext context) => (Address(context), DefaultLimit);
}

// The coarse limit that runs before authentication; it adds no headers, since the policy of the endpoint adds its own.
internal sealed class PreAuthRateLimitPolicy(RateLimitOptions options, TimeProvider time) : FluentCmsRateLimitPolicy(options.PreAuth.PermitLimit, time, reportsHeaders: false)
{
    protected override (string Caller, int Limit) Partition(HttpContext context) => (Address(context), DefaultLimit);
}

internal static class ClientAddress
{
    // An IPv4-mapped address is its IPv4 address, and an IPv6 address is its /64, which is what one subscriber gets.
    public static string Partition(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return $"{new IPAddress(bytes)}/64";
    }
}
