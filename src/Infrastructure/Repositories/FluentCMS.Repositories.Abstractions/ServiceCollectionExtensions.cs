using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Repositories.Abstractions;

/// <summary>
/// Extension methods for IServiceCollection to support database area registration
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Register a DbContext for a specific database area.
    /// This registration will be used later by AddDatabaseManager to configure the actual database provider.
    /// </summary>
    /// <typeparam name="TArea">The database area marker interface</typeparam>
    /// <typeparam name="TContext">The DbContext type for this area</typeparam>
    /// <param name="services">The service collection</param>
    /// <param name="contextConfiguration">Optional DbContext configuration</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddDataContextForArea<TArea, TContext>(this IServiceCollection services, Action<DbContextOptionsBuilder>? contextConfiguration = null)
        where TArea : class, IDatabaseArea
        where TContext : DbContext
    {
        // Register the area with the registry
        DatabaseAreaRegistry.Register<TArea, TContext>(contextConfiguration);

        // Note: We don't register the DbContext here - that will be done by AddDatabaseManager
        // based on the database provider configuration

        return services;
    }

    /// <summary>
    /// Configure database management for all registered areas.
    /// This method processes all areas registered via AddDataContextForArea and configures
    /// their DbContexts with the appropriate database providers.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Database manager configuration</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddDatabaseManager(this IServiceCollection services, Action<DatabaseManagerOptions> configureOptions)
    {
        // Create and configure the options
        var options = new DatabaseManagerOptions(services);
        configureOptions?.Invoke(options);

        // Get all registered areas
        var registrations = DatabaseAreaRegistry.GetRegistrations();

        // Process each registered area
        foreach (var (areaType, registration) in registrations)
        {
            // Get the configuration for this area (specific or default)
            var areaConfig = options.GetConfiguration(areaType);

            if (areaConfig?.DatabaseProvider == null)
            {
                throw new InvalidOperationException(
                    $"No database provider configured for area '{areaType.Name}'. " +
                    "Please configure a provider using Default() or For<{areaType.Name}>() in AddDatabaseManager options.");
            }

            // Register the DbContext with the configured provider
            RegisterDbContextForArea(services, registration, areaConfig);

            // Register data seeding if configured
            if (areaConfig.SeedingOptions != null)
            {
                RegisterDataSeeding(services, areaType, areaConfig.SeedingOptions);
            }
        }

        return services;
    }

    /// <summary>
    /// Register a DbContext for a specific area with the configured database provider
    /// </summary>
    private static void RegisterDbContextForArea(IServiceCollection services, DatabaseAreaRegistration registration, IDatabaseAreaConfiguration areaConfig)
    {
        // Create a composite configuration that combines library and provider options
        void CompositeConfiguration(DbContextOptionsBuilder options)
        {
            // Apply library-specific configuration first
            registration.ContextConfiguration?.Invoke(options);

            // Apply database provider configuration
            areaConfig.DatabaseProvider!.Configure(options, areaConfig.ConnectionString!);

            // Apply provider-specific options last
            areaConfig.ProviderOptions?.Invoke(options);
        }

        // Register the DbContext using reflection to handle generic types
        var dbContextType = registration.ContextType;
        var serviceDescriptor = ServiceDescriptor.Scoped(dbContextType, serviceProvider =>
        {
            var optionsBuilder = new DbContextOptionsBuilder();
            optionsBuilder.UseApplicationServiceProvider(serviceProvider);
            CompositeConfiguration(optionsBuilder);

            // Create DbContext instance using reflection
            return Activator.CreateInstance(dbContextType, optionsBuilder.Options)!;
        });

        services.Add(serviceDescriptor);
    }

    /// <summary>
    /// Register data seeding services for an area
    /// </summary>
    private static void RegisterDataSeeding(IServiceCollection services, Type areaType, DataSeedingOptions seedingOptions)
    {
        // Register the seeding options for this area
        services.AddSingleton(serviceProvider => seedingOptions);

        // Note: Actual seeder registration would be handled by the specific library
        // or discovered through additional registry if needed
    }
}
