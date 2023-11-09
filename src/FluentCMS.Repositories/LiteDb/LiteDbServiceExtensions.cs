using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.LiteDb;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

public static class LiteDbServiceExtensions
{
    public static IServiceCollection AddLiteDbRepositories(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<LiteDbOptions>(config.GetSection("LiteDb"));

        services.AddScoped<LiteDbContext>();

        services.AddScoped(typeof(IGenericRepository<>), typeof(LiteDbGenericRepository<>));

        services.AddScoped<IUserRepository, LiteDbUserRepository>();
        services.AddScoped<IContentTypeRepository, LiteDbContentTypeRepository>();
        services.AddScoped<ISiteRepository, LiteDbSiteRepository>();
        services.AddScoped<IHostRepository, LiteDbHostRepository>();
        services.AddScoped<IPageRepository, LiteDbPageRepository>();

        return services;
    }
}
