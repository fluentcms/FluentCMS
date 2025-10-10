namespace FluentCMS.Infrastructure.Plugins.Loading;

/// <summary>
/// Interface for configuring the middleware pipeline with plugin contributions.
/// Handles priority-based ordering and error handling for plugin middleware registration.
/// </summary>
public interface IPipelineConfigurator
{
    /// <summary>
    /// Configures the middleware pipeline for all plugins in priority order.
    /// Provides each plugin with an isolated service provider for middleware registration.
    /// </summary>
    /// <param name="app">The application builder to configure with plugin middleware.</param>
    /// <param name="plugins">The dependency-ordered list of plugins to configure.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when app, plugins, or serviceProvider is null.</exception>
    Task ConfigurePluginPipeline(IApplicationBuilder app, IReadOnlyList<IPluginStartup> plugins, CancellationToken cancellationToken = default);
}
