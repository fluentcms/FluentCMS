namespace FluentCMS.Diagnostics;

/// <summary>A setting that is fine on a developer machine and not on a server.</summary>
public sealed record InsecureDefault
{
    /// <summary>The FCMS code of the production check that refuses it, or a short name for a default that is only reported.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>What is insecure and how to change it.</summary>
    public string Message { get; init; } = string.Empty;
}
