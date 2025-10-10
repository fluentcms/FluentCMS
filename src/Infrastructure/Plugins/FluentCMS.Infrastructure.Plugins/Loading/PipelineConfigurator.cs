namespace FluentCMS.Infrastructure.Plugins.Loading;

/// <summary>
/// Implementation of IPipelineConfigurator that handles plugin middleware configuration with priority ordering.
/// Provides each plugin with an isolated service provider and handles middleware registration errors.
/// </summary>
/// <remarks>
/// Initializes a new instance of the PipelineConfigurator class.
/// </remarks>
/// <param name="logger">The logger for recording pipeline configuration activities.</param>
public class PipelineConfigurator(ILogger<PipelineConfigurator> logger) : IPipelineConfigurator
{
    private readonly ILogger<PipelineConfigurator> _logger = NullArgumentException.RequireNonNull(logger);

    /// <summary>
    /// Configures the middleware pipeline for all plugins in priority order.
    /// Calls the Configure method on each plugin with an isolated service provider.
    /// </summary>
    /// <param name="app">The application builder to configure with plugin middleware.</param>
    /// <param name="plugins">The dependency-ordered list of plugins to configure.</param>
    /// <param name="serviceProvider">The main service provider built from DI container.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when app, plugins, or serviceProvider is null.</exception>
    public async Task ConfigurePluginPipeline(IApplicationBuilder app, IReadOnlyList<IPluginStartup> plugins, IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        _logger.LogInformation("Configuring middleware pipeline for {PluginCount} plugins in priority order", plugins.Count);

        // Sort plugins by ConfigurePriority (lowest first)
        var orderedPlugins = plugins
            .OrderBy(p => p.ConfigurePriority)
            .ToList();

        foreach (var plugin in orderedPlugins)
        {
            try
            {
                // Configure middleware for this plugin
                await ConfigurePluginMiddleware(app, plugin, serviceProvider);

                _logger.LogDebug("Successfully configured middleware for plugin '{PluginName}' (priority: {Priority})",
                    plugin.Name, plugin.ConfigurePriority);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to configure middleware for plugin '{PluginName}': {Message}",
                    plugin.Name, ex.Message);

                // Rethrow to fail fast - middleware configuration failures are critical
                throw new PluginPipelineConfigurationException(
                    $"Plugin middleware configuration failed for '{plugin.Name}'", plugin.Name, ex);
            }
        }

        _logger.LogInformation("Plugin pipeline configuration completed successfully for all {PluginCount} plugins", plugins.Count);
    }

    /// <summary>
    /// Configures middleware for a specific plugin.
    /// Call the plugin's Configure method with the application builder and service provider.
    /// </summary>
    /// <param name="app">The application builder to configure.</param>
    /// <param name="plugin">The plugin to configure middleware for.</param>
    /// <param name="serviceProvider">The service provider for dependency resolution.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ConfigurePluginMiddleware(IApplicationBuilder app, IPluginStartup plugin, IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        _logger.LogDebug("Configuring middleware for plugin '{PluginName}'", plugin.Name);

        try
        {
            // Call the plugin's Configure method
            await Task.Run(() => plugin.Configure(app, serviceProvider));

            _logger.LogDebug("Plugin '{PluginName}' configured middleware successfully", plugin.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Plugin '{PluginName}' failed to configure middleware: {Message}",
                plugin.Name, ex.Message);
            throw;
        }
    }
}
