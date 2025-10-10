namespace FluentCMS.Infrastructure.Plugins.Lifecycle;

/// <summary>
/// Event fired when a plugin begins loading.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="PluginLoadingEvent"/> class.
/// </remarks>
/// <param name="pluginName">The name of the plugin being loaded.</param>
public sealed class PluginLoadingEvent(string pluginName) : EventBase
{

    /// <summary>
    /// Gets the name of the plugin being loaded.
    /// </summary>
    public string PluginName { get; } = pluginName ?? throw new ArgumentNullException(nameof(pluginName));
}
