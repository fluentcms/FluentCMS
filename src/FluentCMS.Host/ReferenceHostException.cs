namespace FluentCMS.Host;

internal static class ReferenceHostErrorCodes
{
    public const string UnknownDataProvider = "FCMS3150";
    public const string NoConnectionString = "FCMS3151";
    public const string UnknownSample = "FCMS3152";
    public const string SchemaFileNotFound = "FCMS3153";
}

// The reference host's configuration cannot start a host, which stops it before anything else runs.
public sealed class ReferenceHostException : InvalidOperationException
{
    internal ReferenceHostException(string code, string message)
        : base($"{code}: {message}")
    {
        Code = code;
    }

    public string Code { get; }
}
