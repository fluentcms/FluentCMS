namespace FluentCMS.Diagnostics;

/// <summary>A migration that is not applied yet.</summary>
public sealed record StartupMigration
{
    /// <summary>The stream it belongs to: <c>system</c>, <c>content</c> or the name of a plugin.</summary>
    public string Stream { get; init; } = string.Empty;

    /// <summary>The migration id.</summary>
    public string Id { get; init; } = string.Empty;
}
