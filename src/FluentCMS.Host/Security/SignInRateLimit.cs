using System.Globalization;
using System.Threading.RateLimiting;
using FluentCMS.Admin;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace FluentCMS.Host.Security;

public sealed class SignInRateLimitOptions
{
    public const string SectionName = "SignInRateLimit";

    public int PermitLimit { get; set; } = 10;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}

// Limits the posts to pages that take a password, per client address (SEC-07). Identity's lockout limits attempts per account on top.
internal static class SignInRateLimit
{
    private const string PolicyName = "sign-in";

    public static IServiceCollection AddSignInRateLimit(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SignInRateLimitOptions>(configuration.GetSection(SignInRateLimitOptions.SectionName));
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(PolicyName, Partition);
            options.OnRejected = RejectAsync;
        });

        return services;
    }

    public static TBuilder RateLimitCredentialForms<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(endpoint =>
        {
            if (endpoint.Metadata.OfType<AcceptsCredentialsAttribute>().Any())
            {
                endpoint.Metadata.Add(new EnableRateLimitingAttribute(PolicyName));
            }
        });

        return builder;
    }

    // Showing the form is never limited, only submitting it.
    private static RateLimitPartition<string> Partition(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            return RateLimitPartition.GetNoLimiter("*");
        }

        var options = context.RequestServices.GetRequiredService<IOptions<SignInRateLimitOptions>>().Value;
        return RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = options.PermitLimit, Window = options.Window, QueueLimit = 0 });
    }

    // A body of its own, so the status code page does not answer a throttled post with "Not found".
    private static async ValueTask RejectAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        var localizer = context.HttpContext.RequestServices.GetRequiredService<IStringLocalizer<SignInRateLimitOptions>>();
        response.ContentType = "text/plain; charset=utf-8";
        await response.WriteAsync(localizer["Too many attempts. Wait a moment and try again."], cancellationToken);
    }
}
