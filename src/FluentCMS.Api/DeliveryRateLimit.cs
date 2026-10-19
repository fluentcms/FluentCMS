using System.Globalization;
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
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = options.PermitLimit, Window = options.Window, QueueLimit = 0 });
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
