// Namespace for configuration abstractions
namespace FluentCMS.Configuration.Abstractions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

// Static extension methods for adding database-backed configuration options
public static class DatabaseConfigurationExtensions
{
    // Central registry to track sections that should be stored in database (static to allow access before DI container build)
    private static readonly DatabaseConfigurationRegistry _registry = new();

    // Private helper method to perform common registration logic
    private static void RegisterSectionWithServices<TOptions>(IServiceCollection services, string sectionName)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(services);

        if (string.IsNullOrWhiteSpace(sectionName))
            throw new ArgumentException("Section name cannot be null or empty", nameof(sectionName));

        // Register this section for database storage
        _registry.RegisterSection(sectionName, typeof(TOptions));

        services.TryAddSingleton(_registry);
    }

    // Basic registration method without binding
    public static OptionsBuilder<TOptions> AddDatabaseOptions<TOptions>(this IServiceCollection services, string sectionName)
        where TOptions : class
    {
        // Perform common registration logic
        RegisterSectionWithServices<TOptions>(services, sectionName);

        // Return OptionsBuilder for fluent configuration (validation, etc.)
        return services.AddOptions<TOptions>();
    }

    // Convenience method that performs registration and binding from IConfiguration
    public static OptionsBuilder<TOptions> AddDatabaseOptions<TOptions>(this IServiceCollection services, string sectionName, IConfiguration configuration)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Perform common registration logic
        RegisterSectionWithServices<TOptions>(services, sectionName);

        // Return OptionsBuilder with binding already configured
        return services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName));
    }

    // Advanced method for custom binder configuration
    public static OptionsBuilder<TOptions> AddDatabaseOptions<TOptions>(this IServiceCollection services, string sectionName, IConfiguration configuration, Action<BinderOptions>? configureBinder)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Perform common registration logic
        RegisterSectionWithServices<TOptions>(services, sectionName);

        // Return OptionsBuilder with binding and custom binder options
        return services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName), configureBinder);
    }
}


