namespace FluentCMS.Infrastructure.Plugins.Loading;

/// <summary>
/// Main orchestrator for the plugin loading process.
/// Coordinates the three-phase loading: Discovery → ConfigureServices → Configure.
/// </summary>
/// <remarks>
/// Initializes a new instance of the PluginLoader class.
/// </remarks>
/// <param name="pluginScanner">The plugin scanner for discovering plugins.</param>
/// <param name="pluginValidator">The plugin validator for validating dependencies.</param>
/// <param name="serviceRegistrar">The service registrar for DI registration.</param>
/// <param name="pipelineConfigurator">The pipeline configurator for middleware setup.</param>
/// <param name="logger">The logger for recording loading activities.</param>
public class PluginLoader(IPluginScanner pluginScanner, PluginValidator pluginValidator, IServiceRegistrar serviceRegistrar, IPipelineConfigurator pipelineConfigurator, ILogger<PluginLoader> logger) : IPluginLoader
{
    private readonly IPluginScanner _pluginScanner = NullArgumentException.RequireNonNull(pluginScanner);
    private readonly PluginValidator _pluginValidator = NullArgumentException.RequireNonNull(pluginValidator);
    private readonly IServiceRegistrar _serviceRegistrar = NullArgumentException.RequireNonNull(serviceRegistrar);
    private readonly IPipelineConfigurator _pipelineConfigurator = NullArgumentException.RequireNonNull(pipelineConfigurator);
    private readonly ILogger<PluginLoader> _logger = NullArgumentException.RequireNonNull(logger);

    /// <inheritdoc/>
    public async Task<PluginLoadingResult> LoadPlugins(PluginSystemOptions options, IConfiguration hostConfiguration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(hostConfiguration);

        var startTime = DateTimeOffset.Now;
        var errors = new List<PluginLoadingError>();
        var loadedPlugins = new List<PluginInfo>();

        _logger.LogInformation("Starting plugin loading process");

        try
        {
            // Phase 1: Discovery
            var discoveredPlugins = await ExecuteDiscoveryPhase(options, errors, cancellationToken);
            if (errors.Count != 0 && !options.IgnoreErrors)
            {
                return CreateResult(loadedPlugins, errors, startTime);
            }

            // Phase 2: Validation
            var dependencyGraph = await ExecuteValidationPhase(discoveredPlugins, errors, cancellationToken);
            if (errors.Count != 0 && !options.IgnoreErrors)
            {
                return CreateResult(loadedPlugins, errors, startTime);
            }

            // Get sorted plugins for remaining phases
            var sortedPlugins = dependencyGraph?.SortedPlugins ?? discoveredPlugins;

            // Phase 3: Service Registration
            var serviceCollection = new ServiceCollection();
            await ExecuteServiceRegistrationPhase(serviceCollection, sortedPlugins, hostConfiguration, errors, cancellationToken);
            if (errors.Count != 0 && !options.IgnoreErrors)
            {
                return CreateResult(loadedPlugins, errors, startTime);
            }

            // Phase 4: Configure IServiceProviderFactories

            // Build the service provider
            var serviceProvider = serviceCollection.BuildServiceProvider();

            // Phase 5: Pipeline Configuration
            var appBuilder = new ApplicationBuilder(serviceProvider);
            await ExecutePipelineConfigurationPhase(appBuilder, sortedPlugins, serviceProvider, errors, cancellationToken);
            if (errors.Count != 0 && !options.IgnoreErrors)
            {
                return CreateResult(loadedPlugins, errors, startTime);
            }

            // Create success info for loaded plugins
            foreach (var plugin in sortedPlugins)
            {
                loadedPlugins.Add(new PluginInfo(plugin, PluginStatus.Active, DateTimeOffset.Now));
            }

            _logger.LogInformation("Plugin loading process completed successfully with {PluginCount} plugins", loadedPlugins.Count);

            return CreateResult(loadedPlugins, errors, startTime);
        }
        catch (Exception ex)
        {
            // Add general failure error
            errors.Add(new PluginLoadingError(
                PluginLoadingErrorType.GeneralFailure,
                null,
                PluginLoadingPhase.Discovery, // General phase
                $"Unexpected error during plugin loading: {ex.Message}",
                ex));

            _logger.LogError(ex, "Unexpected error during plugin loading process");

            // Always return the result, even with errors, to provide feedback
            return CreateResult(loadedPlugins, errors, startTime);
        }
    }

    /// <summary>
    /// Executes the discovery phase of plugin loading.
    /// Scans assemblies and discovers plugin implementations.
    /// </summary>
    /// <param name="options">The plugin system options.</param>
    /// <param name="errors">The list to add errors to.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The list of discovered plugins.</returns>
    private async Task<IReadOnlyList<IPluginStartup>> ExecuteDiscoveryPhase(PluginSystemOptions options, List<PluginLoadingError> errors, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Executing discovery phase");

            // Apply timeout if specified
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (options.PluginLoadTimeout != Timeout.InfiniteTimeSpan)
            {
                timeoutCts.CancelAfter(options.PluginLoadTimeout);
            }

            var discoveredPlugins = await _pluginScanner.ScanForPlugins(options, timeoutCts.Token);

            _logger.LogDebug("Discovery phase completed with {PluginCount} plugins found", discoveredPlugins.Count);

            return discoveredPlugins;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            errors.Add(new PluginLoadingError(
                PluginLoadingErrorType.Timeout,
                null,
                PluginLoadingPhase.Discovery,
                "Plugin discovery was cancelled"));

            return [];
        }
        catch (Exception ex)
        {
            errors.Add(new PluginLoadingError(
                PluginLoadingErrorType.DiscoveryFailure,
                null,
                PluginLoadingPhase.Discovery,
                $"Plugin discovery failed: {ex.Message}",
                ex));

            return [];
        }
    }

    /// <summary>
    /// Executes the validation phase of plugin loading.
    /// Validates plugin dependencies and configurations.
    /// </summary>
    /// <param name="plugins">The plugins to validate.</param>
    /// <param name="errors">The list to add errors to.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The dependency graph if validation succeeded, null otherwise.</returns>
    private async Task<DependencyGraph?> ExecuteValidationPhase(IReadOnlyList<IPluginStartup> plugins, List<PluginLoadingError> errors, CancellationToken cancellationToken)
    {
        if (plugins.Count == 0)
        {
            return new DependencyGraph([], new Dictionary<string, IReadOnlyList<string>>(), []);
        }

        try
        {
            _logger.LogDebug("Executing validation phase for {PluginCount} plugins", plugins.Count);

            var validationResult = await Task.Run(() =>
                _pluginValidator.ValidatePlugins(plugins), cancellationToken);

            if (!validationResult.IsValid)
            {
                foreach (var error in validationResult.Errors)
                {
                    errors.Add(new PluginLoadingError(
                        PluginLoadingErrorType.ValidationFailure,
                        error.PluginName,
                        PluginLoadingPhase.Validation,
                        error.Message));
                }
            }

            _logger.LogDebug("Validation phase completed with {ErrorCount} errors", validationResult.Errors.Count);

            return validationResult.DependencyGraph;
        }
        catch (Exception ex)
        {
            errors.Add(new PluginLoadingError(
                PluginLoadingErrorType.ValidationFailure,
                null,
                PluginLoadingPhase.Validation,
                $"Plugin validation failed: {ex.Message}",
                ex));

            return null;
        }
    }

    /// <summary>
    /// Executes the service registration phase of plugin loading.
    /// Registers plugin services with the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <param name="plugins">The plugins to register services for.</param>
    /// <param name="hostConfiguration">The host configuration.</param>
    /// <param name="errors">The list to add errors to.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    private async Task ExecuteServiceRegistrationPhase(IServiceCollection services, IReadOnlyList<IPluginStartup> plugins, IConfiguration hostConfiguration, List<PluginLoadingError> errors, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Executing service registration phase for {PluginCount} plugins", plugins.Count);

            await _serviceRegistrar.RegisterPluginServices(services, plugins, hostConfiguration, cancellationToken);

            _logger.LogDebug("Service registration phase completed successfully");
        }
        catch (Exception ex)
        {
            errors.Add(new PluginLoadingError(
                PluginLoadingErrorType.ServiceRegistrationFailure,
                null,
                PluginLoadingPhase.ServiceRegistration,
                $"Service registration failed: {ex.Message}",
                ex));
        }
    }

    /// <summary>
    /// Executes the pipeline configuration phase of plugin loading.
    /// Configures the middleware pipeline with plugin contributions.
    /// </summary>
    /// <param name="app">The application builder for middleware configuration.</param>
    /// <param name="plugins">The plugins to configure pipeline for.</param>
    /// <param name="serviceProvider">The service provider for dependency resolution.</param>
    /// <param name="errors">The list to add errors to.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    private async Task ExecutePipelineConfigurationPhase(
        IApplicationBuilder app,
        IReadOnlyList<IPluginStartup> plugins,
        IServiceProvider serviceProvider,
        List<PluginLoadingError> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Executing pipeline configuration phase for {PluginCount} plugins", plugins.Count);

            await _pipelineConfigurator.ConfigurePluginPipeline(app, plugins, serviceProvider);

            _logger.LogDebug("Pipeline configuration phase completed successfully");
        }
        catch (Exception ex)
        {
            errors.Add(new PluginLoadingError(
                PluginLoadingErrorType.PipelineConfigurationFailure,
                null,
                PluginLoadingPhase.PipelineConfiguration,
                $"Pipeline configuration failed: {ex.Message}",
                ex));
        }
    }

    /// <summary>
    /// Creates a PluginLoadingResult from the current state.
    /// </summary>
    /// <param name="loadedPlugins">The plugins that were successfully loaded.</param>
    /// <param name="errors">Any errors that occurred.</param>
    /// <param name="startTime">When the loading process started.</param>
    /// <returns>The loading result.</returns>
    private static PluginLoadingResult CreateResult(IReadOnlyList<PluginInfo> loadedPlugins, IReadOnlyList<PluginLoadingError> errors, DateTimeOffset startTime)
    {
        var endTime = DateTimeOffset.Now;
        var duration = endTime - startTime;

        return new PluginLoadingResult(loadedPlugins, errors, duration, endTime);
    }
}
