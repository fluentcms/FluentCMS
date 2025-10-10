namespace FluentCMS.Infrastructure.Plugins.Lifecycle;

/// <summary>
/// Event fired when a plugin has finished configuring its middleware.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="PluginConfiguredEvent"/> class.
/// </remarks>
/// <param name="pluginName">The name of the plugin that was configured.</param>
public sealed class PluginConfiguredEvent(string pluginName) : EventBase
{

    /// <summary>
    /// Gets the name of the plugin that was configured.
    /// </summary>
    public string PluginName { get; } = pluginName ?? throw new ArgumentNullException(nameof(pluginName));
}
