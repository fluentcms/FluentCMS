namespace FluentCMS.Infrastructure.Plugins.Loading;

/// <summary>
/// Interface for orchestrating the complete plugin loading process.
/// Coordinates the three-phase loading: Discovery, ConfigureServices, Configure.
/// </summary>
public interface IPluginLoader
{
    /// <summary>
    /// Loads all plugins through the complete three-phase process.
    /// Discovers plugins, validates them, registers services, and configures the pipeline.
    /// </summary>
    /// <param name="options">The plugin system options.</param>
    /// <param name="hostConfiguration">The host application configuration.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation that returns the loading result.</returns>
    Task<PluginLoadingResult> LoadPlugins(PluginSystemOptions options, IConfiguration hostConfiguration, CancellationToken cancellationToken = default);
}
