namespace FluentCMS.Infrastructure.Plugins.Discovery;

/// <summary>
/// Defines the contract for scanning assemblies and discovering plugin implementations.
/// This interface uses the Strategy pattern to allow different scanning implementations.
/// </summary>
public interface IPluginScanner
{
    /// <summary>
    /// Scans assemblies for plugin implementations based on the provided options.
    /// </summary>
    /// <param name="options">The plugin system options containing scanning configuration.</param>
    /// <param name="cancellationToken">Token to cancel the scanning operation.</param>
    /// <returns>A read-only list of discovered plugin startup instances.</returns>
    Task<IReadOnlyList<IPluginStartup>> ScanForPlugins(PluginSystemOptions options, CancellationToken cancellationToken = default);
}
