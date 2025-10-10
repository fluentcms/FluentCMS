namespace FluentCMS.Infrastructure.Plugins.Lifecycle;

/// <summary>
/// Event raised when a plugin is being loaded.
/// Provides information about the current loading phase.
/// </summary>
/// <remarks>
/// Initializes a new instance of the PluginLoadingEvent class.
/// </remarks>
/// <param name="pluginName">The name of the plugin being loaded.</param>
/// <param name="phase">The current loading phase.</param>
/// <param name="context">Additional context information.</param>
public class PluginLoadingEvent(string pluginName, PluginLoadingPhase phase, IReadOnlyDictionary<string, object>? context = null) : EventBase
{
    /// <summary>
    /// Gets the name of the plugin being loaded.
    /// </summary>
    public string PluginName { get; } = NullArgumentException.RequireNonEmptyOrNullString(pluginName);

    /// <summary>
    /// Gets the current loading phase.
    /// </summary>
    public PluginLoadingPhase Phase { get; } = phase;

    /// <summary>
    /// Gets additional context information about the loading process.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Context { get; } = context ?? new Dictionary<string, object>();
}
