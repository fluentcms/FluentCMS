namespace FluentCMS.Infrastructure.Plugins;

/// <summary>
/// Configuration options for the FluentCMS plugin system.
/// These options control how plugins are discovered, loaded, and managed.
/// </summary>
public class PluginSystemOptions
{
    /// <summary>
    /// Gets or sets the assembly name patterns to scan for plugins.
    /// Default patterns: ["FluentCMS.Plugins.*"]
    /// </summary>
    public string[] ScanAssemblyPatterns { get; set; } =
    [
        "FluentCMS.Plugins.*"
    ];

    /// <summary>
    /// Gets or sets a value indicating whether to ignore errors when loading plugins.
    /// When true, plugin loading failures are logged but don't stop the application startup.
    /// When false, any plugin loading failure causes application startup to fail.
    /// Default: false
    /// </summary>
    public bool IgnoreErrors { get; set; } = false;

    /// <summary>
    /// Gets or sets the timeout for plugin loading operations.
    /// Default: 30 seconds
    /// </summary>
    public TimeSpan PluginLoadTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets a value indicating whether to enable resource monitoring.
    /// When enabled, plugins will have their resource usage tracked.
    /// Default: true
    /// </summary>
    public bool EnableResourceMonitoring { get; set; } = true;

    /// <summary>
    /// Gets or sets the interval for resource monitoring checks.
    /// Default: 30 seconds
    /// </summary>
    public TimeSpan ResourceMonitoringInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets a value indicating whether to publish detailed lifecycle events.
    /// When enabled, all plugin lifecycle events are published to the event bus.
    /// Default: true
    /// </summary>
    public bool EnableLifecycleEvents { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether plugins can access the host's configuration.
    /// When enabled, plugins receive scoped configuration sections.
    /// When disabled, plugins operate without access to host configuration.
    /// Default: true
    /// </summary>
    public bool EnableConfigurationAccess { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of plugins to load concurrently.
    /// Default: 5
    /// </summary>
    public int MaxConcurrentPluginLoads { get; set; } = 5;
}
