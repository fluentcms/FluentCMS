namespace FluentCMS.Infrastructure.Plugins;

/// <summary>
/// Represents the current status of a plugin during its lifecycle.
/// </summary>
public enum PluginStatus
{
    /// <summary>
    /// Plugin status is unknown or not yet determined.
    /// </summary>
    Unknown,

    /// <summary>
    /// Plugin has been discovered but not yet loaded.
    /// </summary>
    Discovered,

    /// <summary>
    /// Plugin is currently being loaded and configured.
    /// </summary>
    Loading,

    /// <summary>
    /// Plugin has successfully loaded and is operational.
    /// </summary>
    Loaded,

    /// <summary>
    /// Plugin is configured and registered with DI container.
    /// </summary>
    Configured,

    /// <summary>
    /// Plugin has been initialized and is running normally.
    /// </summary>
    Active,

    /// <summary>
    /// Plugin is being shut down.
    /// </summary>
    Stopping,

    /// <summary>
    /// Plugin has been stopped.
    /// </summary>
    Stopped,

    /// <summary>
    /// Plugin failed to load or configure properly.
    /// </summary>
    Failed
}
