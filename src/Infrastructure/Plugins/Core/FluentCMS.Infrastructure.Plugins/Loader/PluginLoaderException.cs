namespace FluentCMS.Infrastructure.Plugins.Loader;

/// <summary>
/// Exception thrown when plugin loading fails.
/// </summary>
public class PluginLoaderException : Exception
{
    /// <summary>
    /// Initializes a new instance of the PluginLoaderException class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public PluginLoaderException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the PluginLoaderException class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public PluginLoaderException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
