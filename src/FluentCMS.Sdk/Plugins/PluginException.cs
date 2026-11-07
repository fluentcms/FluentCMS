namespace FluentCMS.Plugins;

/// <summary>
/// Thrown by a plugin to refuse a request with one of its codes. The HTTP layer answers with the status and title the plugin
/// gave the code with <see cref="IPluginServiceBuilder.AddErrorCode"/>, and 422 for a code it did not add.
/// </summary>
public sealed class PluginException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="code">The code.</param>
    /// <param name="message">A message for people.</param>
    public PluginException(string code, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentException.ThrowIfNullOrEmpty(message);
        Code = code;
    }

    /// <summary>The code the plugin gave.</summary>
    public string Code { get; }
}
