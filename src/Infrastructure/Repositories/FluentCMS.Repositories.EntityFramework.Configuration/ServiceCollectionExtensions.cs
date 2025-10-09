using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.DataInitialization;
using FluentCMS.Repositories.DataInitialization.Abstractions;
using FluentCMS.Repositories.EntityFramework.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FluentCMS.Repositories.EntityFramework.Configuration;

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

        services.AddScoped<ISchemaValidatorService, SchemaValidatorService>();
        services.AddScoped<AuditableEntitySaveChangesInterceptor>();
        services.AddScoped<DomainEventSaveChangesInterceptor>();
        services.AddScoped<IDataSeederService, DataSeederService>();
        services.AddSingleton<IHostedService, DataInitializerHostedService>();

        return services;
    }

    /// <summary>
    /// Registers a DbContext with the appropriate database configuration
    /// The configuration is determined by checking if the DbContext implements any marker interfaces
    /// Falls back to the default configuration if no marker is found
    /// </summary>
    /// <typeparam name="TContext">The DbContext type to register</typeparam>
    /// <param name="services">The service collection</param>
    /// <param name="action">Optional action to configure additional DbContext options</param>
    /// <returns>The service collection for chaining</returns>
    /// <exception cref="InvalidOperationException">Thrown when DatabaseManager hasn't been configured</exception>
    public static IServiceCollection AddDatabaseContext<TContext, TMarker>(this IServiceCollection services, Action<DbContextOptionsBuilder>? action = null)
        where TContext : DbContext
        where TMarker : IDatabaseArea
    {
        ArgumentNullException.ThrowIfNull(services);

        CheckStaticOptions();

        // Get the appropriate configuration for this DbContext type
        var config = _staticOptions!.GetConfigurationForMarker(typeof(TMarker));

        // Register the DbContext with the configuration
        services.AddDbContext<TContext>(
            (serviceProvider, builder) =>
            {
                var auditInterceptor = serviceProvider.GetRequiredService<AuditableEntitySaveChangesInterceptor>();
                var domainEventInterceptor = serviceProvider.GetRequiredService<DomainEventSaveChangesInterceptor>();
                builder.AddInterceptors(auditInterceptor, domainEventInterceptor);
                // Apply the database provider configuration (e.g., UseSqlite, UseSqlServer)
                config.Apply(builder);
                action?.Invoke(builder);
            });

        return services;
    }

    /// <summary>
    /// Registers a DbContext with the appropriate database configuration
    /// The configuration is determined by checking if the DbContext implements any marker interfaces
    /// Falls back to the default configuration if no marker is found
    /// </summary>
    /// <typeparam name="TContext">The DbContext type to register</typeparam>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for chaining</returns>
    /// <exception cref="InvalidOperationException">Thrown when DatabaseManager hasn't been configured</exception>
    public static IServiceCollection AddDatabaseContext<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        CheckStaticOptions();

        // Get the default configuration for this DbContext type
        var config = _staticOptions!.GetDefaultConfiguration();

        // Register the DbContext with the default configuration
        services.AddDbContext<TContext>(
            (serviceProvider, builder) =>
            {
                var auditInterceptor = serviceProvider.GetRequiredService<AuditableEntitySaveChangesInterceptor>();
                var domainEventInterceptor = serviceProvider.GetRequiredService<DomainEventSaveChangesInterceptor>();
                builder.AddInterceptors(auditInterceptor, domainEventInterceptor);
                // Apply the database provider configuration (e.g., UseSqlite, UseSqlServer)
                config.Apply(builder);
            });

        return services;
    }

    /// <summary>
    /// Enables and configures data seeding for the database
    /// </summary>
    /// <param name="builder">The database configuration builder</param>
    /// <param name="configure">Action to configure seeding options</param>
    /// <returns>The configuration builder for chaining</returns>
    public static DatabaseConfigurationBuilder EnableDataSeeding(this DatabaseConfigurationBuilder builder, Action<DataSeedingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new DataSeedingOptions();
        configure(options);

        builder.Configuration.SeedingOptions = options;

        return builder;
    }

    /// <summary>
    /// Enables and configures database schema validation
    /// </summary>
    /// <param name="builder">The database configuration builder</param>
    /// <param name="configure">Action to configure schema validation options</param>
    /// <returns>The configuration builder for chaining</returns>
    public static DatabaseConfigurationBuilder EnableSchemaValidation(this DatabaseConfigurationBuilder builder, Action<SchemaValidationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SchemaValidationOptions();
        configure(options);

        builder.Configuration.SchemaValidationOptions = options;

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
        where TMarker : class
    {
        ArgumentNullException.ThrowIfNull(services);

        CheckStaticOptions();

        // Register the seeder with the marker type as the key
        services.AddKeyedScoped<IDataSeeder, TSeeder>(typeof(TMarker));
        // Also register the seeder with the "Default" key to allow resolution in both contexts.
        // This is intentional to support scenarios where the seeder may be resolved by either key.
        services.AddDataSeeder<TSeeder>();

        return services;
    }

    /// <summary>
    /// Registers a data seeder for the default database
    /// </summary>
    /// <typeparam name="TSeeder">The data seeder implementation type</typeparam>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddDataSeeder<TSeeder>(this IServiceCollection services)
        where TSeeder : class, IDataSeeder
    {
        ArgumentNullException.ThrowIfNull(services);

        CheckStaticOptions();

        // Register the seeder with "Default" as the key
        services.AddKeyedScoped<IDataSeeder, TSeeder>("Default");

        return services;
    }

    /// <summary>
    /// Registers a schema validator for the default database
    /// </summary>
    /// <typeparam name="TValidator">The schema validator implementation type</typeparam>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddSchemaValidator<TValidator>(this IServiceCollection services)
        where TValidator : class, ISchemaValidator
    {
        ArgumentNullException.ThrowIfNull(services);

        CheckStaticOptions();

        // Register the validator with "Default" as the key
        services.AddKeyedScoped<ISchemaValidator, TValidator>("Default");

        return services;
    }

    /// <summary>
    /// Registers a schema validator for a specific database marker
    /// </summary>
    /// <typeparam name="TValidator">The schema validator implementation type</typeparam>
    /// <typeparam name="TMarker">The database marker interface type</typeparam>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddSchemaValidator<TValidator, TMarker>(this IServiceCollection services)
        where TValidator : class, ISchemaValidator
        where TMarker : class
    {
        ArgumentNullException.ThrowIfNull(services);

        CheckStaticOptions();

        // Register the validator with the marker type as the key
        services.AddKeyedScoped<ISchemaValidator, TValidator>(typeof(TMarker));

        // Also register the validator as the "Default" keyed instance.
        // This allows the same validator to be resolved both by marker type and as the default,
        // providing flexibility for consumers who may not specify a marker.
        // See CodeQL nitpick: registering the same validator twice is intentional for this pattern.
        services.AddSchemaValidator<TValidator>();

        return services;
    }

    private static void CheckStaticOptions()
    {
        // If we still don't have options, throw an exception
        if (_staticOptions == null)
        {
            throw new InvalidOperationException(
                "DatabaseManager has not been configured. Call AddDatabaseManager before registering DbContexts.");
        }
    }
}
