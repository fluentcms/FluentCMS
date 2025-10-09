using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FluentCMS.Configuration.Abstractions;

/// <summary>
/// Extension methods for adding database configuration
/// </summary>
public static class DatabaseConfigurationExtensions
{
    private static readonly DatabaseConfigurationRegistry _registry = new();

    /// <summary>
    /// Registers a configuration section to be stored in database and configures IOptions binding.
    /// Returns OptionsBuilder for fluent configuration (validation, post-configuration, etc.)
    /// </summary>
    /// <typeparam name="TOptions">The options type to configure</typeparam>
    /// <param name="services">The service collection</param>
    /// <param name="sectionName">Configuration section name (e.g., "EmailSettings")</param>
    /// <returns>OptionsBuilder for fluent configuration</returns>
    public static OptionsBuilder<TOptions> AddDatabaseOptions<TOptions>(this IServiceCollection services, string sectionName)
        where TOptions : class
    {
        // Register this section for database storage
        _registry.RegisterSection(sectionName, typeof(TOptions));

        services.TryAddSingleton(_registry);

        // Return OptionsBuilder for fluent configuration (validation, etc.)
        return services.AddOptions<TOptions>();
    }

    /// <summary>
    /// Registers a configuration section to be stored in database and binds it to IOptions.
    /// Convenience method that also performs the binding.
    /// </summary>
    /// <typeparam name="TOptions">The options type to configure</typeparam>
    /// <param name="services">The service collection</param>
    /// <param name="sectionName">Configuration section name</param>
    /// <param name="configuration">Configuration to bind from</param>
    /// <returns>OptionsBuilder for fluent configuration</returns>
    public static OptionsBuilder<TOptions> AddDatabaseOptions<TOptions>(this IServiceCollection services, string sectionName, IConfiguration configuration)
        where TOptions : class
    {
        // Register this section for database storage
        _registry.RegisterSection(sectionName, typeof(TOptions));

        services.TryAddSingleton(_registry);

        // Return OptionsBuilder with binding already configured
        return services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName));
    }

    /// <summary>
    /// Registers a configuration section to be stored in database and binds it with custom configuration.
    /// </summary>
    /// <typeparam name="TOptions">The options type to configure</typeparam>
    /// <param name="services">The service collection</param>
    /// <param name="sectionName">Configuration section name</param>
    /// <param name="configuration">Configuration to bind from</param>
    /// <param name="configureBinder">Action to configure the binder</param>
    /// <returns>OptionsBuilder for fluent configuration</returns>
    public static OptionsBuilder<TOptions> AddDatabaseOptions<TOptions>(this IServiceCollection services, string sectionName, IConfiguration configuration, Action<BinderOptions>? configureBinder)
        where TOptions : class
    {
        // Register this section for database storage
        _registry.RegisterSection(sectionName, typeof(TOptions));

        services.TryAddSingleton(_registry);

        // Return OptionsBuilder with binding and custom binder options
        return services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName), configureBinder);
    }
}
