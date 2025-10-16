namespace FluentCMS.Infrastructure.Configuration;

// Static extension methods for adding database-backed configuration options
// These methods integrate with the ASP.NET Core Options pattern and register sections
// for database storage through the DatabaseConfigurationRegistry
public static class DatabaseConfigurationExtensions
{
    // Lock object to ensure thread-safe registry creation
    // Prevents race conditions when multiple threads access GetOrCreateRegistry simultaneously
    private static readonly Lock _registryLock = new();

    // Private helper method to perform common registration logic
    // Gets or creates a registry instance specific to this IServiceCollection
    // This ensures each service collection has its own isolated registry (no static state)
    private static void RegisterSectionWithServices<TOptions>(IServiceCollection services, string sectionName)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(services);

        if (string.IsNullOrWhiteSpace(sectionName))
            throw new ArgumentException("Section name cannot be null or whitespace", nameof(sectionName));

        // Get or create the registry instance for this service collection
        // This approach ensures each IServiceCollection has its own registry instance
        // avoiding static state and supporting multi-tenant scenarios
        var registry = GetOrCreateRegistry(services);

        // Register this section for database storage
        // Will throw InvalidOperationException if already registered with different type
        registry.RegisterSection(sectionName, typeof(TOptions));
    }

    // Gets the registry from the service collection or creates and registers a new one
    // This ensures each service collection has exactly one registry instance
    // Thread-safe: Uses lock to prevent race conditions during concurrent access
    private static DatabaseConfigurationRegistry GetOrCreateRegistry(IServiceCollection services)
    {
        // Use lock to ensure atomic check-and-create operation
        // This prevents multiple threads from creating duplicate registry instances
        lock (_registryLock)
        {
            // Check if registry is already registered in the service collection
            var registryDescriptor = services.FirstOrDefault(d =>
                d.ServiceType == typeof(DatabaseConfigurationRegistry));

            if (registryDescriptor?.ImplementationInstance is DatabaseConfigurationRegistry existingRegistry)
            {
                // Registry already exists, return it
                return existingRegistry;
            }

            // Create a new registry instance and register it as singleton
            var newRegistry = new DatabaseConfigurationRegistry();
            services.AddSingleton(newRegistry);

            return newRegistry;
        }
    }

    public static IServiceCollection AddDatabaseConfigurationRegistry(this IServiceCollection services)
    {
        // Ensure the registry is created and registered
        GetOrCreateRegistry(services);
        return services;
    }

    // Registers options class for database storage without binding to configuration
    // Use this when you want to manually configure options or use other configuration sources
    // Returns OptionsBuilder to allow fluent chaining of additional configuration (e.g., validation)
    public static OptionsBuilder<TOptions> AddDbOptions<TOptions>(this IServiceCollection services, string sectionName)
        where TOptions : class
    {
        // Register the section name and options type with the registry
        // This marks the section for database storage by the configuration provider
        RegisterSectionWithServices<TOptions>(services, sectionName);

        // Return OptionsBuilder to enable fluent configuration of validation, post-configuration, etc.
        return services.AddOptions<TOptions>();
    }

    // Registers options class for database storage and binds to IConfiguration section
    // This is the most common usage pattern - combines registration and configuration binding
    // Returns OptionsBuilder to allow fluent chaining of additional configuration
    public static OptionsBuilder<TOptions> AddDbOptions<TOptions>(this IServiceCollection services, string sectionName, IConfiguration configuration)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Register the section name and options type with the registry
        RegisterSectionWithServices<TOptions>(services, sectionName);

        // Bind the options to the specified configuration section
        // This populates the options class with values from IConfiguration
        return services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName));
    }

    // Registers options class for database storage with custom binding configuration
    // Use this when you need to customize how configuration binds to the options class
    // configureBinder allows control over binding behavior (e.g., error handling, binding flags)
    // Returns OptionsBuilder to allow fluent chaining of additional configuration
    public static OptionsBuilder<TOptions> AddDbOptions<TOptions>(this IServiceCollection services, string sectionName, IConfiguration configuration, Action<BinderOptions>? configureBinder)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Register the section name and options type with the registry
        RegisterSectionWithServices<TOptions>(services, sectionName);

        // Bind the options to the specified configuration section with custom binder options
        // configureBinder can customize binding behavior like error handling on missing properties
        return services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName), configureBinder);
    }
}
