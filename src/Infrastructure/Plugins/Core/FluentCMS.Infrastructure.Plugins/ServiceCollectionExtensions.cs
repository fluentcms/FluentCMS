namespace FluentCMS.Infrastructure.Plugins;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the plugin system services to the service collection.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configureOptions">An optional action to configure plugin system options.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddPluginSystem(this IServiceCollection services, Action<PluginSystemOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Configure plugin system options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<PluginSystemOptions>(options => { /* Use defaults */ });
        }
        // Register core plugin services
        services.AddTransient<IPluginDiscovery, PluginDiscovery>();
        services.AddTransient<IPluginLoader, PluginLoader>();
        services.AddTransient<IPluginInitializer, PluginInitializer>();
        return services;
    }
}
