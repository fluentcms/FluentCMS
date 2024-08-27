using FluentCMS.Providers.ApiTokenProviders;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceExtensions
{
    public static IServiceCollection AddDefaultApiTokenProvider(this IServiceCollection services)
    {
        services.AddScoped<IApiTokenProvider, DefaultApiTokenProvider>();
        return services;
    }
}
