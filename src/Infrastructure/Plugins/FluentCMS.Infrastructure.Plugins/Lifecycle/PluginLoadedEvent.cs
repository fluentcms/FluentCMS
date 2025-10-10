namespace FluentCMS.Infrastructure.Plugins.Lifecycle;

/// <summary>
/// Event raised when a plugin has successfully loaded.
/// </summary>
/// <remarks>
/// Initializes a new instance of the PluginLoadedEvent class.
/// </remarks>
/// <param name="pluginName">The name of the plugin that was loaded.</param>
/// <param name="pluginVersion">The version of the plugin.</param>
/// <param name="dependencies">The dependencies of the plugin.</param>
/// <param name="loadedAt">When the plugin was loaded.</param>
public class PluginLoadedEvent(string pluginName, string pluginVersion, IReadOnlyList<string> dependencies, DateTimeOffset loadedAt) : EventBase
{
    /// <summary>
    /// Gets the name of the plugin that was loaded.
    /// </summary>
    public string PluginName { get; } = NullArgumentException.RequireNonEmptyOrNullString(pluginName);

    /// <summary>
    /// Gets the version of the plugin.
    /// </summary>
    public string PluginVersion { get; } = NullArgumentException.RequireNonEmptyOrNullString(pluginVersion);

    /// <summary>
    /// Gets the dependencies of the plugin.
    /// </summary>
    public IReadOnlyList<string> Dependencies { get; } = NullArgumentException.RequireNonNull(dependencies);

    /// <summary>
    /// Gets the time when the plugin was loaded.
    /// </summary>
    public DateTimeOffset LoadedAt { get; } = loadedAt;
}
