namespace FluentCMS.Core.DependencyInjection;

// A host is set up in a way that FluentCMS cannot start with.
public sealed class FluentCmsConfigurationException : InvalidOperationException
{
    internal FluentCmsConfigurationException(string code, string message)
        : base($"{code}: {message}")
    {
        Code = code;
        Detail = message;
    }

    public string Code { get; }

    internal string Detail { get; }
}
