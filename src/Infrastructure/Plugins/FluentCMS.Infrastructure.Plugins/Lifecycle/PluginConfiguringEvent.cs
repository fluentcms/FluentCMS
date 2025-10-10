namespace FluentCMS.Infrastructure.Plugins.Lifecycle;

/// <summary>
/// Event fired when a plugin is about to have its middleware configured.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="PluginConfiguringEvent"/> class.
/// </remarks>
/// <param name="pluginName">The name of the plugin being configured.</param>
public sealed class PluginConfiguringEvent(string pluginName) : EventBase
{

    /// <summary>
    /// Gets the name of the plugin being configured.
    /// </summary>
    public string PluginName { get; } = NullArgumentException.RequireNonEmptyOrNullString(pluginName);
}
