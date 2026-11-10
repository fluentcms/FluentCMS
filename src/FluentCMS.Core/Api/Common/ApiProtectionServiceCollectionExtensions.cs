using FluentCMS.Core.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Api.Common;

internal static class ApiProtectionServiceCollectionExtensions
{
    // The policies, CORS and limits that UseFluentCms puts in the pipeline.
    public static IServiceCollection AddFluentCmsApiProtection(this IServiceCollection services)
    {
        services.AddFluentCmsOptions(RateLimitOptions.Section, HostErrorCodes.InvalidOption, new RateLimitOptions(), limits => limits.Validate());
        services.AddFluentCmsOptions(RequestLimitsOptions.Section, HostErrorCodes.InvalidOption, new RequestLimitsOptions());
        services.AddFluentCmsCors();
        services.AddSingleton<ApiPipelineState>();
        services.AddSingleton(provider => new PreAuthRateLimitPolicy(provider.GetRequiredService<RateLimitOptions>(), provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton(provider => new HealthRateLimitPolicy(provider.GetRequiredService<RateLimitOptions>(), provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton(provider => new AuthRateLimitPolicy(provider.GetRequiredService<RateLimitOptions>(), provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<PreAuthRateLimiter>();

        // UseFluentCms adds UseAuthorization, which needs these whether or not the host has policies of its own.
        services.AddAuthorization();
        services.AddRateLimiter(options => options.RejectionStatusCode = StatusCodes.Status429TooManyRequests);
        services.AddOptions<RateLimiterOptions>().Configure<RateLimitOptions, TimeProvider, AuthRateLimitPolicy>((options, limits, time, auth) =>
        {
            var delivery = new DeliveryRateLimitPolicy(limits, time);
            var management = new ManagementRateLimitPolicy(limits, time);
            options.AddPolicy<string>(RateLimitPolicyNames.Delivery, delivery);
            options.AddPolicy<string>(RateLimitPolicyNames.Management, management);
            options.AddPolicy<string>(RateLimitPolicyNames.Caller, new CallerRateLimitPolicy(delivery, management));
            options.AddPolicy<string>(RateLimitPolicyNames.Auth, auth);
        });

        return services;
    }
}
