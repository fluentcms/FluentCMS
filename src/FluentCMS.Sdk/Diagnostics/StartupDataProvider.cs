namespace FluentCMS.Diagnostics;

/// <summary>The database provider of a host and how it starts.</summary>
public sealed record StartupDataProvider
{
    /// <summary>The name the provider gives itself, such as <c>SQLite</c>.</summary>
    public string Provider { get; init; } = string.Empty;

    /// <summary>The version of the provider package.</summary>
    public string ProviderVersion { get; init; } = string.Empty;

    /// <summary>Whether startup applies the system and plugin migrations.</summary>
    public bool MigrateOnStartup { get; init; }

    /// <summary>Whether schema sync is <c>Auto</c> or <c>Off</c>.</summary>
    public string SchemaSync { get; init; } = string.Empty;
}
