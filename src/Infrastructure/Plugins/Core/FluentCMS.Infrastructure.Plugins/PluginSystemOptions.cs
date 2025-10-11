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


    public ILoggerFactory LoggerFactory { get; set; } = default!;
}
