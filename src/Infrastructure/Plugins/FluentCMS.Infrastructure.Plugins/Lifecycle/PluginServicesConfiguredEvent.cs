namespace FluentCMS.Infrastructure.Plugins.Lifecycle;

/// <summary>
/// Event fired when a plugin's services have been configured.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="PluginServicesConfiguredEvent"/> class.
/// </remarks>
/// <param name="pluginName">The name of the plugin whose services were configured.</param>
public sealed class PluginServicesConfiguredEvent(string pluginName) : EventBase
{

    /// <summary>
    /// Gets the name of the plugin whose services were configured.
    /// </summary>
    public string PluginName { get; } = NullArgumentException.RequireNonEmptyOrNullString(pluginName);
}
