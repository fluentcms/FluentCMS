using FluentCMS.Infrastructure.Plugins;
using FluentCMS.Infrastructure.Plugins.Discovery;
using FluentCMS.Infrastructure.Plugins.Lifecycle;
using FluentCMS.Infrastructure.Plugins.Loading;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Extension methods for integrating the plugin system into ASP.NET Core applications.
/// Provides fluent API for configuring and loading plugins.
/// </summary>
public static class PluginServiceCollectionExtensions
{
    /// <summary>
    /// Adds the FluentCMS plugin system to the service collection.
    /// Registers all core services required for plugin loading and management.
    /// </summary>
    /// <param name="services">The service collection to add plugin services to.</param>
    /// <param name="configureOptions">Optional action to configure plugin system options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when services is null.</exception>
    public static IServiceCollection AddPluginSystem(this IServiceCollection services, Action<PluginSystemOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Configure options
        var options = new PluginSystemOptions();
        configureOptions?.Invoke(options);

        // Register core plugin services
        services.AddSingleton(options);
        services.AddTransient<IPluginScanner, PluginScanner>();
        services.AddTransient<IServiceRegistrar, ServiceRegistrar>();
        services.AddTransient<IPipelineConfigurator, PipelineConfigurator>();
        services.AddTransient<IPluginLoader, PluginLoader>();
        services.AddTransient<DependencyGraphBuilder>();
        services.TryAddTransient<PluginValidator>();
        services.TryAddTransient<ILifecycleEventPublisher, LifecycleEventPublisher>();

        // Register plugin registry (will be populated during loading)
        services.AddSingleton<IPluginRegistry>(provider =>
        {
            // This will be updated after plugin loading completes
            // For now, return an implementation that can be replaced
            return new PluginRegistry();
        });

        // Register resource quota monitor (optional feature)
        services.AddSingleton<IResourceQuotaMonitor, ResourceQuotaMonitor>();

        return services;
    }

    /// <summary>
    /// Adds plugin loading middleware to the application pipeline.
    /// This middleware loads and initializes plugins during application startup.
    /// Should be called after all services are registered and before the application starts.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when app is null.</exception>
    public static IApplicationBuilder UsePluginSystem(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Load plugins during application startup
        LoadPlugins(app);

        return app;
    }

    /// <summary>
    /// Loads and initializes plugins.
    /// This method is called automatically by UsePluginSystem but can also be called manually for more control.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="configureOptions">Optional action to configure loading behavior.</param>
    /// <returns>The plugin loading result.</returns>
    public static PluginLoadingResult LoadPlugins(this IApplicationBuilder app, Action<PluginSystemOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        var serviceProvider = app.ApplicationServices;
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("PluginSystem");
        var hostConfiguration = serviceProvider.GetRequiredService<IConfiguration>();

        try
        {
            logger.LogInformation("Initializing FluentCMS Plugin System");

            // Get or configure options
            var options = serviceProvider.GetRequiredService<PluginSystemOptions>();
            configureOptions?.Invoke(options);

            // Load plugins
            var pluginLoader = serviceProvider.GetRequiredService<IPluginLoader>();
            var result = pluginLoader.LoadPlugins(options, app, hostConfiguration).GetAwaiter().GetResult();

            // Publish application started event
            var lifecyclePublisher = serviceProvider.GetService<ILifecycleEventPublisher>();
            if (lifecyclePublisher != null)
            {
                lifecyclePublisher.PublishApplicationStarted(result.LoadedPlugins.Count, result.LoadingDuration).GetAwaiter().GetResult();
            }

            // Log summary
            if (result.Errors.Any())
            {
                logger.LogWarning("Plugin system loaded with {LoadedCount} plugins and {ErrorCount} errors",
                    result.LoadedPlugins.Count, result.Errors.Count);

                foreach (var error in result.Errors)
                {
                    logger.LogWarning("Plugin loading error: {Message} (Phase: {Phase})",
                        error.Message, error.Phase);
                }
            }
            else
            {
                logger.LogInformation("Plugin system successfully loaded {PluginCount} plugins in {Duration}ms",
                    result.LoadedPlugins.Count, result.LoadingDuration.TotalMilliseconds);
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Critical error during plugin system initialization: {Message}", ex.Message);
            throw;
        }
    }
}
