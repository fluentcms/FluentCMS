namespace FluentCMS.Infrastructure.Plugins.Loading;

/// <summary>
/// Exception thrown when plugin service registration fails.
/// </summary>
/// <remarks>
/// Initializes a new instance of the PluginServiceRegistrationException class.
/// </remarks>
/// <param name="message">The error message.</param>
/// <param name="pluginName">The name of the plugin that failed.</param>
/// <param name="innerException">The inner exception that caused the failure.</param>
public class PluginServiceRegistrationException(string message, string pluginName, Exception innerException) : Exception(message, innerException)
{
    /// <summary>
    /// Gets the name of the plugin that failed to register services.
    /// </summary>
    public string PluginName { get; } = NullArgumentException.RequireNonEmptyOrNullString(pluginName, nameof(pluginName));
}
