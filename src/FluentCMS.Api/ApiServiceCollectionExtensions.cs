using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Api;

public static class ApiServiceCollectionExtensions
{
    // The delivery API reads through Core services that Infrastructure registers, so it adds none of its own yet.
    public static IServiceCollection AddFluentCmsApi(this IServiceCollection services)
    {
        return services;
    }
}
