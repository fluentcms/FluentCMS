using FluentCMS.Repository.Abstractions;
using FluentCMS.Repository.InMemoryDB;

namespace Microsoft.Extensions.DependencyInjection;

public static class InMemoryDBExtensions
{
    public static IServiceCollection AddInMemoryDB(this IServiceCollection services)
    {
        services.AddScoped(typeof(InMemoryDBContext<>));
        services.AddScoped(typeof(InMemoryDBContext<,>));
        services.AddScoped(typeof(IGenericRepository<,>), typeof(InMemoryGenericRepository<,>));
        services.AddScoped(typeof(IGenericRepository<>), typeof(InMemoryGenericRepository<>));
        return services;

    }
}