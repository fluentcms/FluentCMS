namespace FluentCMS.Infrastructure.Plugins.Lifecycle;

/// <summary>
/// Event raised when a plugin fails to load.
/// </summary>
/// <remarks>
/// Initializes a new instance of the PluginFailedEvent class.
/// </remarks>
/// <param name="pluginName">The name of the plugin that failed.</param>
/// <param name="errorMessage">The error message describing the failure.</param>
/// <param name="exception">The exception that caused the failure.</param>
/// <param name="phase">The phase where the failure occurred.</param>
public class PluginFailedEvent(string pluginName, string errorMessage, Exception? exception, PluginLoadingPhase phase) : EventBase
{
    /// <summary>
    /// Gets the name of the plugin that failed to load.
    /// </summary>
    public string PluginName { get; } = NullArgumentException.RequireNonEmptyOrNullString(pluginName);

    /// <summary>
    /// Gets the error message describing the failure.
    /// </summary>
    public string ErrorMessage { get; } = NullArgumentException.RequireNonEmptyOrNullString(errorMessage);

    /// <summary>
    /// Gets the exception that caused the failure, if available.
    /// </summary>
    public Exception? Exception { get; } = exception;

    /// <summary>
    /// Gets the phase where the failure occurred.
    /// </summary>
    public PluginLoadingPhase Phase { get; } = phase;
}
