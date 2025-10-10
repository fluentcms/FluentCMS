namespace FluentCMS.Infrastructure.Plugins.Loading;

/// <summary>
/// Exception thrown when plugin pipeline configuration fails.
/// </summary>
/// <remarks>
/// Initializes a new instance of the PluginPipelineConfigurationException class.
/// </remarks>
/// <param name="message">The error message.</param>
/// <param name="pluginName">The name of the plugin that failed.</param>
/// <param name="innerException">The inner exception that caused the failure.</param>
public class PluginPipelineConfigurationException(string message, string? pluginName, Exception innerException) : Exception(message, innerException)
{
    /// <summary>
    /// Gets the name of the plugin that failed to configure middleware.
    /// </summary>
    public string? PluginName { get; } = pluginName;
}
