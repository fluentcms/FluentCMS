using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Repositories.Abstractions;

// IServiceCollection extension
public static class ServiceCollectionExtensions
{
    // Register a custom DbContext for an area with configuration options
    public static IServiceCollection AddDataContextForArea<TArea, TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder<TContext>>? config = null)
        where TContext : DbContext
    {
        // Register the DbContext with EF Core
        services.AddDbContext<TContext>(config != null
            ? options => config((DbContextOptionsBuilder<TContext>)options)
            : null);

        // Register as the data context for the area (via generic type)
        services.AddScoped(typeof(TArea), sp => sp.GetRequiredService<TContext>());

        return services;
    }

    // Helper for libraries to register their data seeder
    public static IServiceCollection AddDataSeeder<TDataSeeder, TArea>(this IServiceCollection services)
        where TDataSeeder : class, IDataSeeder<TArea>
        where TArea : IDatabaseArea
    {
        services.AddScoped<IDataSeeder<TArea>, TDataSeeder>();
        services.AddScoped<IDataSeeder, TDataSeeder>();
        return services;
    }

    // Create a query specification for fluent querying
    public static IQuerySpecification<TEntity> CreateQuerySpecification<TEntity>(this DbContext dbContext)
        where TEntity : class
    {
        var dbSet = dbContext.Set<TEntity>();
        return new QuerySpecification<TEntity>(dbSet);
    }
}
