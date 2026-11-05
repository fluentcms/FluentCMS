namespace FluentCMS.Diagnostics;

/// <summary>An assembly of FluentCMS loaded in the process.</summary>
public sealed record StartupAssembly
{
    /// <summary>The assembly name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Its version.</summary>
    public string Version { get; init; } = string.Empty;
}
