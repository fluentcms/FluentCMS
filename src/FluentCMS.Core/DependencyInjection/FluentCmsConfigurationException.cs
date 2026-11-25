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

    // A value of an option the host cannot start with (FCMS1101), as the validate of AddFluentCmsOptions throws it.
    public static FluentCmsConfigurationException InvalidOption(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new(HostErrorCodes.InvalidOption, message);
    }

    // Something the host needs is missing, such as a call that registers a package's services or a file it serves (FCMS1103).
    public static FluentCmsConfigurationException NotRegistered(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new(HostErrorCodes.NotRegistered, message);
    }

    // A file the host serves is there but not in a form it can serve, such as a page whose <base href> a package sets (FCMS1107).
    public static FluentCmsConfigurationException MalformedFile(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new(HostErrorCodes.MalformedFile, message);
    }
}
