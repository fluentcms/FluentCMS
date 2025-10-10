namespace FluentCMS.Infrastructure.Plugins.Loading;

/// <summary>
/// Represents an error that occurred during plugin loading.
/// </summary>
/// <remarks>
/// Initializes a new instance of PluginLoadingError.
/// </remarks>
/// <param name="errorType">The type of loading error.</param>
/// <param name="pluginName">The plugin associated with the error.</param>
/// <param name="phase">The phase where the error occurred.</param>
/// <param name="message">A human-readable description of the error.</param>
/// <param name="exception">The exception that caused the error.</param>
public class PluginLoadingError(PluginLoadingErrorType errorType, string? pluginName, PluginLoadingPhase phase, string message, Exception? exception = null)
{
    /// <summary>
    /// Gets the type of loading error.
    /// </summary>
    public PluginLoadingErrorType ErrorType { get; } = errorType;

    /// <summary>
    /// Gets the plugin associated with the error, if applicable.
    /// </summary>
    public string? PluginName { get; } = pluginName;

    /// <summary>
    /// Gets the phase where the error occurred.
    /// </summary>
    public PluginLoadingPhase Phase { get; } = phase;

    /// <summary>
    /// Gets a human-readable description of the error.
    /// </summary>
    public string Message { get; } = NullArgumentException.RequireNonEmptyOrNullString(message);

    /// <summary>
    /// Gets the exception that caused the error, if available.
    /// </summary>
    public Exception? Exception { get; } = exception;
}
