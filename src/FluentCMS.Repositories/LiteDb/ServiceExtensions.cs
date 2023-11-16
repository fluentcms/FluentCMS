using FluentCMS.Repositories;
using FluentCMS.Repositories.LiteDb;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

public static class LiteDbServiceExtensions
{
    public static IServiceCollection AddLiteDbRepositories(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<LiteDbOptions>(options =>
        {
            options.ConnectionString = config.GetConnectionString("LiteDb");
        });
        services.AddScoped<LiteDbContext>();
        ConfigureServices(services);
        return services;
    }

    public static IServiceCollection AddInMemoryLiteDbRepositories(this IServiceCollection services)
    {
        services.Configure<LiteDbOptions>(options => { });
        services.AddScoped<LiteDbContext>();
        ConfigureServices(services);

        return services;
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));

        services.AddScoped<ISiteRepository, SiteRepository>();
        services.AddScoped<IHostRepository, HostRepository>();
        services.AddScoped<IPageRepository, PageRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
    }
}
