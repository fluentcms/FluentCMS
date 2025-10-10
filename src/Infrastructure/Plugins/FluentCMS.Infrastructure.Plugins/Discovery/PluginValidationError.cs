namespace FluentCMS.Infrastructure.Plugins.Discovery;

/// <summary>
/// Represents a single validation error with detailed information.
/// </summary>
/// <remarks>
/// Initializes a new instance of PluginValidationError.
/// </remarks>
/// <param name="errorType">The type of validation error.</param>
/// <param name="pluginName">The name of the plugin associated with the error.</param>
/// <param name="message">A human-readable description of the error.</param>
/// <param name="details">Additional details about the error.</param>
public class PluginValidationError(PluginValidationErrorType errorType, string? pluginName, string message, IReadOnlyDictionary<string, object>? details = null)
{
    /// <summary>
    /// Gets the type of validation error.
    /// </summary>
    public PluginValidationErrorType ErrorType { get; } = errorType;

    /// <summary>
    /// Gets the plugin associated with the error, if applicable.
    /// </summary>
    public string? PluginName { get; } = pluginName;

    /// <summary>
    /// Gets a human-readable description of the error.
    /// </summary>
    public string Message { get; } = NullArgumentException.RequireNonEmptyOrNullString(message);

    /// <summary>
    /// Gets additional details about the error.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Details { get; } = details;
}
