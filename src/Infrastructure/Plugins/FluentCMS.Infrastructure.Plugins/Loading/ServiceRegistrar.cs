namespace FluentCMS.Infrastructure.Plugins.Loading;

/// <summary>
/// Implementation of IServiceRegistrar that handles plugin service registration with priority ordering.
/// Provides scoped configuration sections and handles DI registration errors gracefully.
/// </summary>
/// <remarks>
/// Initializes a new instance of the ServiceRegistrar class.
/// </remarks>
/// <param name="logger">The logger for recording service registration activities.</param>
public class ServiceRegistrar(ILogger<ServiceRegistrar> logger) : IServiceRegistrar
{
    /// <inheritdoc/>
    public async Task RegisterPluginServices(IServiceCollection services, IReadOnlyList<IPluginStartup> plugins, IConfiguration hostConfiguration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(hostConfiguration);

        logger.LogInformation("Registering services for {PluginCount} plugins in priority order", plugins.Count);

        // Sort plugins by ConfigureServicesPriority (lowest first)
        var orderedPlugins = plugins
            .OrderBy(p => p.ConfigureServicesPriority)
            .ToList();

        foreach (var plugin in orderedPlugins)
        {
            try
            {
                // Create scoped configuration for this plugin
                var scopedConfiguration = CreateScopedConfiguration(hostConfiguration, plugin.Name);

                // Register plugin services
                await ConfigurePluginServices(services, plugin, scopedConfiguration, cancellationToken);

                logger.LogDebug("Successfully registered services for plugin '{PluginName}' (priority: {Priority})",
                    plugin.Name, plugin.ConfigureServicesPriority);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to register services for plugin '{PluginName}': {Message}",
                    plugin.Name, ex.Message);

                // Rethrow to fail fast - service registration failures are critical
                throw new PluginServiceRegistrationException(
                    $"Plugin service registration failed for '{plugin.Name}'", plugin.Name, ex);
            }
        }

        logger.LogInformation("Plugin service registration completed successfully for all {PluginCount} plugins", plugins.Count);
    }

    /// <inheritdoc/>
    public async Task ConfigurePluginServices(IServiceCollection services, IPluginStartup plugin, IConfiguration? scopedConfiguration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(plugin);

        logger.LogDebug("Configuring services for plugin '{PluginName}'", plugin.Name);

        try
        {
            // Call the plugin's ConfigureServices method
            await Task.Run(() => plugin.ConfigureServices(services, scopedConfiguration), cancellationToken);

            logger.LogDebug("Plugin '{PluginName}' configured services successfully", plugin.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Plugin '{PluginName}' failed to configure services: {Message}", plugin.Name, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Creates a scoped configuration section for a specific plugin.
    /// The configuration is scoped under "Plugins:{PluginName}" in the host configuration.
    /// </summary>
    /// <param name="hostConfiguration">The host application configuration.</param>
    /// <param name="pluginName">The name of the plugin to scope configuration for.</param>
    /// <returns>A configuration section scoped to the plugin.</returns>
    private IConfiguration? CreateScopedConfiguration(IConfiguration hostConfiguration, string pluginName)
    {
        var pluginSectionPath = $"Plugins:{pluginName}";

        // Create a sectioned configuration that points to the plugin's configuration section
        var pluginSection = hostConfiguration.GetSection(pluginSectionPath);

        // If no plugin-specific section exists, return an empty configuration
        // This allows plugins to work without explicit configuration
        if (!pluginSection.Exists())
        {
            logger.LogDebug("No configuration section found for plugin '{PluginName}' at path '{Path}', using empty configuration", pluginName, pluginSectionPath);
            return null;
        }

        logger.LogDebug("Using configuration section '{Path}' for plugin '{PluginName}'", pluginSectionPath, pluginName);

        return pluginSection;
    }
}
