using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FluentCMS.Api;

public sealed class DeliveryRateLimitOptions
{
    public const string SectionName = "Delivery:RateLimit";

    public int PermitLimit { get; set; } = 600;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}

// Limits the delivery API per client address (SEC-07). Its own rejection answers with a problem, so a throttled client reads the
// same shape as every other API error, not the plain text the sign-in forms get.
public sealed class DeliveryRateLimit : IRateLimiterPolicy<string>
{
    public const string PolicyName = "delivery";

    public const string RateLimited = "rate_limited";

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected { get; } = RejectAsync;

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        var options = httpContext.RequestServices.GetRequiredService<IOptions<DeliveryRateLimitOptions>>().Value;
        return RateLimitPartition.GetFixedWindowLimiter(
            ClientKey(httpContext.Connection.RemoteIpAddress),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = options.PermitLimit, Window = options.Window, QueueLimit = 0 });
    }

    // An IPv6 client usually holds a whole /64, so it is counted by that prefix; by its full address it could take a fresh one per
    // request. Behind a trusted proxy the address is the client's, as the host's forwarded headers set it.
    public static string ClientKey(IPAddress? address)
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

        var prefix = address.GetAddressBytes();
        Array.Clear(prefix, 8, 8);
        return $"{new IPAddress(prefix)}/64";
    }

    private static async ValueTask RejectAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        var retry = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? (int)Math.Ceiling(retryAfter.TotalSeconds) : (int?)null;
        if (retry is { } seconds)
        {
            http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        }

        await Results.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            detail: retry is { } wait ? $"Too many requests from this address; try again in {wait} seconds." : "Too many requests from this address; try again shortly.",
            extensions: new Dictionary<string, object?>
            {
                ["errors"] = new[] { new { Code = RateLimited, Message = "Too many requests from this address." } },
            }).ExecuteAsync(http);
    }
}
