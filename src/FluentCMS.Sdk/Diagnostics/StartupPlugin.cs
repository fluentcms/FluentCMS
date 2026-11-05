namespace FluentCMS.Diagnostics;

/// <summary>A plugin the host runs.</summary>
public sealed record StartupPlugin
{
    /// <summary>The plugin name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Its version.</summary>
    public string Version { get; init; } = string.Empty;
}
