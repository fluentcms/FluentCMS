using FluentCMS.Repositories.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FluentCMS.Repositories;

/// <summary>
/// Extension methods for IServiceCollection to configure the DatabaseManager
/// </summary>
public static class ServiceCollectionExtensions
{
    // Static instance to store options when services haven't been built yet
    private static DatabaseManagerOptions? _staticOptions;

    /// <summary>
    /// Configures the DatabaseManager with database providers and connection strings
    /// This must be called before registering any DbContexts
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Action to configure database options</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddDatabaseManager(this IServiceCollection services, Action<DatabaseManagerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        // Create and configure options
        var options = new DatabaseManagerOptions(services);
        configure(options);

        // Store options statically for use during DbContext registration
        _staticOptions = options;

        // Register the options as a singleton so it can be retrieved if needed
        services.AddSingleton(options);

        services.AddScoped<IDataSeederService, DataSeederService>();
        services.AddScoped<IDataMigrationService, DataMigrationService>();

        return services;
    }

    /// <summary>
    /// Registers a DbContext with the appropriate database configuration
    /// The configuration is determined by checking if the DbContext implements any marker interfaces
    /// Falls back to the default configuration if no marker is found
    /// </summary>
    /// <typeparam name="TContext">The DbContext type to register</typeparam>
    /// <param name="services">The service collection</param>
    /// <param name="lifetime">The service lifetime (default is Scoped)</param>
    /// <returns>The service collection for chaining</returns>
    /// <exception cref="InvalidOperationException">Thrown when DatabaseManager hasn't been configured</exception>
    public static IServiceCollection AddDatabaseContext<TContext>(this IServiceCollection services, ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        // Try to get options from the static instance first (available during startup)
        var options = _staticOptions;

        // If static options aren't available, try to build service provider and get from DI
        // This handles the case where AddDatabaseContext is called after the service provider is built
        // TODO: is this correct approach?
        if (options == null)
        {
            var serviceProvider = services.BuildServiceProvider();
            options = serviceProvider.GetService<DatabaseManagerOptions>();
        }

        // If we still don't have options, throw an exception
        if (options == null)
        {
            throw new InvalidOperationException(
                "DatabaseManager has not been configured. Call AddDatabaseManager before registering DbContexts.");
        }

        // Get the appropriate configuration for this DbContext type
        var config = options.GetConfigurationForContext(typeof(TContext));

        // Register the DbContext with the configuration
        services.AddDbContext<TContext>(
            (serviceProvider, builder) =>
            {
                // Apply the database provider configuration (e.g., UseSqlite, UseSqlServer)
                config.Apply(builder);
            },
            lifetime);

        return services;
    }

    /// <summary>
    /// Enables and configures data seeding for the database
    /// </summary>
    /// <param name="builder">The database configuration builder</param>
    /// <param name="configure">Action to configure seeding options</param>
    /// <returns>The configuration builder for chaining</returns>
    public static IDatabaseConfigurationBuilder EnableDataSeeding(this IDatabaseConfigurationBuilder builder, Action<DataSeedingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        // Register the hosted service to seed the database at startup
        // Avoid multiple registration for multiple calls of AddDbOptions
        builder.ServiceDescriptors.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DataInitializerHostedService>());

        var options = new DataSeedingOptions();
        configure(options);

        builder.Configuration.SeedingOptions = options;

        return builder;
    }

    public static IDatabaseConfigurationBuilder EnableMigration(this IDatabaseConfigurationBuilder builder, Action<DataMigrationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        // Register the hosted service to seed the database at startup
        // Avoid multiple registration for multiple calls of AddDbOptions
        builder.ServiceDescriptors.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DataInitializerHostedService>());

        var options = new DataMigrationOptions();
        configure(options);

        builder.Configuration.MigrationOptions = options;

        return builder;
    }

    /// <summary>
    /// Registers a data seeder for a specific database marker
    /// </summary>
    /// <typeparam name="TSeeder">The data seeder implementation type</typeparam>
    /// <typeparam name="TMarker">The database marker interface type</typeparam>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddDataSeeder<TSeeder, TMarker>(this IServiceCollection services)
        where TSeeder : class, IDataSeeder
        where TMarker : IDatabaseArea
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register the seeder with the marker type as the key
        services.AddKeyedScoped<IDataSeeder, TSeeder>(typeof(TMarker));
        services.AddKeyedScoped<IDataSeeder, TSeeder>("Default");

        return services;
    }

    public static IServiceCollection AddDataMigration<TDataMigration, TMarker>(this IServiceCollection services)
       where TDataMigration : class, IDataMigration
       where TMarker : IDatabaseArea
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register the migration with the marker type as the key
        services.AddKeyedScoped<IDataMigration, TDataMigration>(typeof(TMarker));
        services.AddKeyedScoped<IDataMigration, TDataMigration>("Default");

        return services;
    }

}
