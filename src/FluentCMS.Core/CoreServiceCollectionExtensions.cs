using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core;

public static class CoreServiceCollectionExtensions
{
    // The composition seam for domain and application services; each epic adds its registrations here.
    public static IServiceCollection AddFluentCmsCore(this IServiceCollection services)
    {
        return services;
    }
}
