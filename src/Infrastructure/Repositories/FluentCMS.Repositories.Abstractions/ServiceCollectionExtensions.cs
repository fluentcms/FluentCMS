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
    // Static registry for database manager configurations
    private static readonly Dictionary<Type, DatabaseAreaConfiguration> s_areaConfigurations = [];
    private static DatabaseAreaConfiguration? s_defaultConfiguration;
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

        // Store configurations in static registry
        s_areaConfigurations.Clear();

        // Store default configuration if set
        s_defaultConfiguration = options.DefaultConfiguration;

        // Store area-specific configurations
        foreach (var kvp in options.AreaConfigurations)
        {
            s_areaConfigurations[kvp.Key] = kvp.Value;
        }

        // Register database initializer for manual seeding
        services.AddScoped<IDataInitializer, DatabaseInitializer>();

        return services;
    }
    // Register a custom DbContext for an area with configuration options
    public static IServiceCollection AddDataContextForArea<TArea, TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder<TContext>>? config = null)
        where TContext : DbContext
    {
        // Create combined configuration action that applies both user config and database manager config
        Action<DbContextOptionsBuilder> combinedConfig = options =>
        {
            // Cast to typed options builder for user config
            var typedOptions = (DbContextOptionsBuilder<TContext>)options;

            // First apply user-provided configuration (if any)
            if (config != null)
            {
                config(typedOptions);
            }

            // Then apply database manager configuration (which overrides user config)
            var areaType = typeof(TArea);

            // Check for area-specific configuration first
            if (s_areaConfigurations.TryGetValue(areaType, out var areaConfig))
            {
                if (areaConfig.DatabaseProvider != null && areaConfig.ConnectionString != null)
                {
                    areaConfig.DatabaseProvider.Configure(options, areaConfig.ConnectionString);
                }
            }
            // Otherwise check for default configuration
            else if (s_defaultConfiguration != null &&
                     s_defaultConfiguration.DatabaseProvider != null &&
                     s_defaultConfiguration.ConnectionString != null)
            {
                s_defaultConfiguration.DatabaseProvider.Configure(options, s_defaultConfiguration.ConnectionString);
            }
        };

        // Register the DbContext with EF Core using combined configuration
        services.AddDbContext<TContext>(combinedConfig);

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
