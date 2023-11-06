using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Api;

public static class MappingExtensions
{
    public static IServiceCollection AddMappingProfiles(this IServiceCollection services)
    {
        services.AddAutoMapper(typeof(MappingExtensions).Assembly);

        return services;
    }
}
