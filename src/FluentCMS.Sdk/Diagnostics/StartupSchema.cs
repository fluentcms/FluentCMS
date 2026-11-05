namespace FluentCMS.Diagnostics;

/// <summary>The schema in code set against the one the content tables were last brought up to.</summary>
public sealed record StartupSchema
{
    /// <summary>The hash of the schema in code.</summary>
    public string Hash { get; init; } = string.Empty;

    /// <summary>The hash of the schema last applied, or null while the schema history is empty or could not be read.</summary>
    public string? AppliedHash { get; init; }

    /// <summary>When the last schema was applied.</summary>
    public DateTimeOffset? AppliedAt { get; init; }

    /// <summary>The FluentCMS version that applied it.</summary>
    public string? AppliedVersion { get; init; }

    /// <summary>Whether the schema in code differs from the one applied.</summary>
    public bool Drifted { get; init; }

    /// <summary>What differs when it does, or why the history could not be read.</summary>
    public string? Changes { get; init; }
}
