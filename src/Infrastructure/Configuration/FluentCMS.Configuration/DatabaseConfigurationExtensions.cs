using FluentCMS.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Configuration;

/// <summary>
/// Extension methods for adding database configuration
/// </summary>
public static class DatabaseConfigurationExtensions
{
    /// <summary>
    /// Adds database configuration provider to the configuration builder.
    /// Automatically discovers all sections registered via AddDatabaseOptions.
    /// </summary>
    /// <param name="builder">Configuration builder</param>
    /// <param name="dbOptions">Database context options</param>
    /// <param name="reloadInterval">Optional reload interval for automatic updates</param>
    public static IConfigurationBuilder AddDatabaseConfiguration(
        this IConfigurationBuilder builder,
        DbContextOptions<ConfigurationDbContext> dbOptions,
        TimeSpan? reloadInterval = null)
    {
        return builder.Add(new DatabaseConfigurationSource
        {
            DbOptions = dbOptions,
            ReloadInterval = reloadInterval ?? TimeSpan.Zero
        });
    }

    /// <summary>
    /// Adds database configuration provider with SQLite.
    /// Automatically discovers all sections registered via AddDatabaseOptions.
    /// </summary>
    /// <param name="builder">Configuration builder</param>
    /// <param name="connectionString">SQLite connection string</param>
    /// <param name="reloadInterval">Optional reload interval for automatic updates</param>
    public static IConfigurationBuilder AddDatabaseConfiguration(
        this IConfigurationBuilder builder,
        string connectionString,
        TimeSpan? reloadInterval = null)
    {
        var dbOptions = new DbContextOptionsBuilder<ConfigurationDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return builder.AddDatabaseConfiguration(dbOptions, reloadInterval);
    }

    /// <summary>
    /// Registers the DatabaseConfigurationProvider as a service for direct access after configuration is built.
    /// This allows services to access the provider for runtime configuration updates.
    /// Call this after AddDatabaseConfiguration.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Built configuration containing the database provider</param>
    public static IServiceCollection AddDatabaseConfigurationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Find the database configuration provider from the built configuration
        var configRoot = configuration as IConfigurationRoot;
        var dbProvider = configRoot?.Providers
            .OfType<DatabaseConfigurationProvider>()
            .FirstOrDefault();

        if (dbProvider != null)
        {
            // Register the existing provider instance as a singleton
            services.AddSingleton(dbProvider);
        }

        return services;
    }

    /// <summary>
    /// Adds database configuration seeding hosted service
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="dbOptions">Database context options</param>
    /// <param name="seedConfiguration">Configuration source for seeding</param>
    /// <param name="dynamicSections">Sections to seed and manage in database</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddDatabaseConfigurationSeeding(
        this IServiceCollection services,
        DbContextOptions<ConfigurationDbContext> dbOptions,
        IConfiguration? seedConfiguration = null,
        List<string>? dynamicSections = null)
    {
        // Register the DbContext for the seeding service
        services.AddDbContext<ConfigurationDbContext>(options =>
        {
            // Copy configuration from the provided options
            foreach (var extension in dbOptions.Extensions)
            {
                ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(extension);
            }
        });

        // Register the seeding options
        services.AddSingleton(new ConfigurationSeedingOptions
        {
            SeedConfiguration = seedConfiguration,
            DynamicSections = dynamicSections ?? DatabaseConfigurationRegistry.GetRegisteredSections().Select(kvp => kvp.Key).ToList()
        });

        // Register the hosted service for seeding
        services.AddHostedService<ConfigurationSeedingHostedService>();

        return services;
    }
}
