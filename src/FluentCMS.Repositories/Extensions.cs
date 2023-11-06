using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.LiteDb;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Repositories;

public static class Extensions
{
    public static IServiceCollection AddLiteDbInMemoryRepository(this IServiceCollection services)
    {
        services.AddScoped(p => new LiteDbContext(new LiteDbOptions()));

        services.AddScoped(typeof(IGenericRepository<>), typeof(LiteDbGenericRepository<>));
        services.AddScoped<IContentTypeRepository, LiteDbContentTypeRepository>();
        services.AddScoped<IUserRepository, LiteDbUserRepository>();
        services.AddScoped<ISiteRepository, LiteDbSiteRepository>();
        services.AddScoped<IPageRepository, LiteDbPageRepository>();

        return services;
    }

    public static IServiceCollection AddLiteDbRepository(this IServiceCollection services, string connectionString)
    {
        services.AddScoped(p => new LiteDbContext(new LiteDbOptions { ConnectionString = connectionString }));

        services.AddScoped(typeof(IGenericRepository<>), typeof(LiteDbGenericRepository<>));
        services.AddScoped<IContentTypeRepository, LiteDbContentTypeRepository>();
        services.AddScoped<IUserRepository, LiteDbUserRepository>();
        services.AddScoped<ISiteRepository, LiteDbSiteRepository>();
        services.AddScoped<IPageRepository, LiteDbPageRepository>();

        return services;
    }
}
