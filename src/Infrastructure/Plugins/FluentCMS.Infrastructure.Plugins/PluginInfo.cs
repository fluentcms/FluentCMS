namespace FluentCMS.Infrastructure.Plugins;

/// <summary>
/// Contains runtime information about a loaded plugin.
/// This class is immutable and thread-safe.
/// </summary>
public sealed class PluginInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginInfo"/> class.
    /// </summary>
    /// <param name="startup">The plugin startup instance.</param>
    /// <param name="status">The current status of the plugin.</param>
    /// <param name="loadedAt">When the plugin was loaded.</param>
    /// <param name="errorMessage">Optional error message if the plugin failed to load.</param>
    public PluginInfo(IPluginStartup startup, PluginStatus status, DateTimeOffset loadedAt, string? errorMessage = null)
    {
        ArgumentNullException.ThrowIfNull(startup);

        Startup = startup;
        Status = status;
        LoadedAt = loadedAt;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Gets the plugin startup instance.
    /// </summary>
    public IPluginStartup Startup { get; }

    /// <summary>
    /// Gets the name of the plugin.
    /// </summary>
    public string Name => Startup.Name;

    /// <summary>
    /// Gets the version of the plugin.
    /// </summary>
    public string Version => Startup.Version;

    /// <summary>
    /// Gets the current status of the plugin.
    /// </summary>
    public PluginStatus Status { get; }

    /// <summary>
    /// Gets when the plugin was loaded.
    /// </summary>
    public DateTimeOffset LoadedAt { get; }

    /// <summary>
    /// Gets the error message if the plugin failed to load (null otherwise).
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Gets whether the plugin loaded successfully.
    /// </summary>
    public bool IsSuccessful => Status != PluginStatus.Failed;

    /// <summary>
    /// Gets whether the plugin is currently active.
    /// </summary>
    public bool IsActive => Status == PluginStatus.Active;

    /// <summary>
    /// Creates a copy of this PluginInfo with a new status.
    /// </summary>
    /// <param name="newStatus">The new status.</param>
    /// <returns>A new PluginInfo instance with the updated status.</returns>
    public PluginInfo WithStatus(PluginStatus newStatus)
    {
        return new PluginInfo(Startup, newStatus, LoadedAt, ErrorMessage);
    }

    /// <summary>
    /// Creates a copy of this PluginInfo marked as failed with an error message.
    /// </summary>
    /// <param name="errorMessage">The error message describing why the plugin failed.</param>
    /// <returns>A new PluginInfo instance marked as failed.</returns>
    public PluginInfo WithError(string errorMessage)
    {
        ArgumentException.ThrowIfNullOrEmpty(errorMessage);
        return new PluginInfo(Startup, PluginStatus.Failed, LoadedAt, errorMessage);
    }
}
