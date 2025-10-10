namespace FluentCMS.Infrastructure.Plugins.Loading;

/// <summary>
/// Interface for registering plugin services with the dependency injection container.
/// Handles priority-based ordering and configuration scoping for plugin service registration.
/// </summary>
public interface IServiceRegistrar
{
    /// <summary>
    /// Registers services for all plugins in priority order.
    /// Each plugin receives a scoped configuration section under "Plugins:{PluginName}".
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <param name="plugins">The dependency-ordered list of plugins to register.</param>
    /// <param name="hostConfiguration">The host application configuration.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when services, plugins, or hostConfiguration is null.</exception>
    Task RegisterPluginServices(IServiceCollection services, IReadOnlyList<IPluginStartup> plugins, IConfiguration hostConfiguration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Configures plugin-specific service collections and settings.
    /// Called for each plugin individually to allow isolated service registration.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="plugin">The plugin to register services for.</param>
    /// <param name="scopedConfiguration">Configuration scoped to this plugin.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ConfigurePluginServices(IServiceCollection services, IPluginStartup plugin, IConfiguration? scopedConfiguration, CancellationToken cancellationToken = default);
}
