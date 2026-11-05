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

        services.AddRateLimiter(options => options.RejectionStatusCode = StatusCodes.Status429TooManyRequests);
        services.AddOptions<RateLimiterOptions>().Configure<RateLimitOptions>((options, limits) =>
        {
            options.AddPolicy<string>(RateLimitPolicyNames.Delivery, new DeliveryRateLimitPolicy(limits));
            options.AddPolicy<string>(RateLimitPolicyNames.Management, new ManagementRateLimitPolicy(limits));
            options.AddPolicy<string>(RateLimitPolicyNames.Auth, new AuthRateLimitPolicy(limits));
        });

        return services;
    }
}
