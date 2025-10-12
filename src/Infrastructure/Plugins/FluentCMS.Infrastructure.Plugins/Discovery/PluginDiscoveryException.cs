namespace FluentCMS.Infrastructure.Plugins.Discovery;

/// <summary>
/// Exception thrown when plugin discovery fails.
/// </summary>
public class PluginDiscoveryException : Exception
{
    /// <summary>
    /// Initializes a new instance of the PluginDiscoveryException class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public PluginDiscoveryException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the PluginDiscoveryException class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public PluginDiscoveryException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
