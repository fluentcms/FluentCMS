using FluentCMS.Providers.ApiTokenProviders;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceExtensions
{
    public static IServiceCollection AddJwtApiTokenProvider(this IServiceCollection services)
    {
        services.AddScoped<IApiTokenProvider, JwtApiTokenProvider>();
        services.AddOptions<JwtApiTokenConfig>()
            .BindConfiguration("Providers:ApiToken:JwtApiTokenConfig");
        return services;
    }
}
