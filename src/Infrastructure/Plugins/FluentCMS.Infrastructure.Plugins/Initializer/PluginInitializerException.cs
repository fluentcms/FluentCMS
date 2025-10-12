namespace FluentCMS.Infrastructure.Plugins.Initializer;

public class PluginInitializerException : Exception
{
    public PluginInitializerException(string message) : base(message)
    {
    }

    public PluginInitializerException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
