// IServiceCollection extensions for database management and data contexts
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FluentCMS.Repositories.Abstractions.Configuration;
using FluentCMS.Repositories.Abstractions.Services;

namespace FluentCMS.Repositories.Abstractions;

/// <summary>
/// Extension methods for configuring database management and data contexts.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds centralized database management for multiple areas with provider-specific configurations.
    /// This replaces individual AddDataContextForArea calls with a declarative configuration approach.
    /// </summary>
    /// <param name="services">The service collection to configure</param>
    /// <param name="configureOptions">Configuration action for database manager options</param>
    /// <returns>The service collection for chaining</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddDatabaseManager(options =>
    /// {
    ///     // Default database for most libraries
    ///     options.Default()
    ///         .UseSqlite(connectionString)
    ///         .EnableDataSeeding(seedingOptions =>
    ///         {
    ///             seedingOptions.IgnoreExceptions = false;
    ///             seedingOptions.Conditions.Add(new EnvironmentCondition(builder.Environment, e => e.IsDevelopment()));
    ///         });
    ///
    ///     // Specific database for ToDo library
    ///     options.For<ITodoDatabaseMarker>()
    ///         .UseSqlServer("DataSource=todo.db;Cache=Shared")
    ///         .EnableDataSeeding(seedingOptions =>
    ///         {
    ///             seedingOptions.IgnoreExceptions = false;
    ///             seedingOptions.Conditions.Add(new EnvironmentCondition(builder.Environment, e => e.IsDevelopment()));
    ///         });
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddDatabaseManager(this IServiceCollection services, Action<DatabaseManagerOptions> configureOptions)
    {
        var options = new DatabaseManagerOptions();
        configureOptions(options);

        // implement here 

        return services;
    }
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
