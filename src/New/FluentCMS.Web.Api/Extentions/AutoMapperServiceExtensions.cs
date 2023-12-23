using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Web.Api.Extentions;
public static class AutoMapperServiceExtensions
{
    public static IServiceCollection AddAutoMapper(this IServiceCollection services)
    {
        services.AddAutoMapper(typeof(AutoMapperServiceExtensions));
        return services;
    }
}
