using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Api;

public static class ApiServiceCollectionExtensions
{
    // The delivery API arrives with F-API; until then this registers nothing.
    public static IServiceCollection AddFluentCmsApi(this IServiceCollection services)
    {
        return services;
    }
}
