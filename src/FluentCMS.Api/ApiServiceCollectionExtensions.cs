using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Api;

public static class ApiServiceCollectionExtensions
{
    // The delivery API reads through Core services that Infrastructure registers; it adds its rate limit policy (SEC-07).
    public static IServiceCollection AddFluentCmsApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DeliveryRateLimitOptions>()
            .Bind(configuration.GetSection(DeliveryRateLimitOptions.SectionName))
            .Validate(options => options.PermitLimit >= 1 && options.Window > TimeSpan.Zero, "Delivery__RateLimit__PermitLimit must be at least 1 and Delivery__RateLimit__Window longer than zero.")
            .ValidateOnStart();
        services.AddRateLimiter(options => options.AddPolicy<string, DeliveryRateLimit>(DeliveryRateLimit.PolicyName));
        return services;
    }
}
