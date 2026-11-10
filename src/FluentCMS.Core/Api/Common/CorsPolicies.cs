using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Idempotency;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Api.Common;

internal static class CorsPolicies
{
    public const string Delivery = "fluentcms-delivery";
    public const string Management = "fluentcms-management";

    private static readonly string[] ExposedHeaders = ["ETag", "Location", "Retry-After", "RateLimit-Limit", "RateLimit-Remaining", "RateLimit-Reset"];

    // An empty list adds no origin to the policy, so no response carries a CORS header.
    public static IServiceCollection AddFluentCmsCors(this IServiceCollection services)
    {
        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<SecurityOptions>((cors, security) =>
        {
            cors.AddPolicy(Delivery, policy => policy
                .WithOrigins([.. security.Cors.Delivery.AllowedOrigins])
                .WithMethods("GET", "HEAD")
                .WithHeaders("Authorization", "X-Api-Key", "X-Cms-Site", "X-Cms-Locale", "If-None-Match", "Content-Type")
                .WithExposedHeaders(ExposedHeaders)
                .SetPreflightMaxAge(TimeSpan.FromMinutes(10)));

            cors.AddPolicy(Management, policy => policy
                .WithOrigins([.. security.Cors.Management.AllowedOrigins])
                .WithMethods("GET", "HEAD", "POST", "PUT", "PATCH", "DELETE")
                .WithHeaders("Authorization", "X-Api-Key", "X-Cms-Site", "X-Cms-Locale", "If-Match", "If-None-Match", "Content-Type", IdempotencyStore.Header)
                .WithExposedHeaders([.. ExposedHeaders, IdempotentRequests.ReplayedHeader])
                .SetPreflightMaxAge(TimeSpan.FromMinutes(10)));
        });

        return services;
    }
}
