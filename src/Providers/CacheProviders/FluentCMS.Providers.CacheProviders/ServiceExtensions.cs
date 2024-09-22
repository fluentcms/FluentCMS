using FluentCMS.Providers.CacheProviders;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceExtensions
{
    public static IServiceCollection AddInMemoryCacheProvider(this IServiceCollection services)
    {
        services.AddScoped<ICacheProvider, InMemoryCacheProvider>();
        services.AddMemoryCache();
        return services;
    }
}
