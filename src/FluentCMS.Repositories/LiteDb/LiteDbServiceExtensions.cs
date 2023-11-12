using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.LiteDb;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

public static class LiteDbServiceExtensions
{
    public static IServiceCollection AddLiteDbRepositories(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<LiteDbOptions>(config.GetSection("LiteDb"));
        services.AddScoped<LiteDbContext>();
        ConfigureServices(services);
        return services;
    }
    public static IServiceCollection AddInMemoryLiteDbRepositories(this IServiceCollection services)
    {
        services.AddScoped<LiteDbContext>(_ => new LiteDbContext(new LiteDbOptions() { ConnectionString = string.Empty }));
        ConfigureServices(services);

        return services;
    }


    private static void ConfigureServices(IServiceCollection services)
    {


        services.AddScoped(typeof(IGenericRepository<>), typeof(LiteDbGenericRepository<>));

        services.AddScoped<IContentTypeRepository, LiteDbContentTypeRepository>();
        services.AddScoped<ISiteRepository, LiteDbSiteRepository>();
        services.AddScoped<IHostRepository, LiteDbHostRepository>();
        services.AddScoped<IPageRepository, LiteDbPageRepository>();
    }

}
