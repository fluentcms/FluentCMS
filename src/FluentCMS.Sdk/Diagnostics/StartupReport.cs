namespace FluentCMS.Diagnostics;

/// <summary>
/// What the host knows about itself once it has started: made once per start, after the migrations, logged and served by
/// the diagnostics endpoint. It holds no connection string, key or password.
/// </summary>
public sealed record StartupReport
{
    /// <summary>When the report was made.</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>The name of the hosting environment, such as <c>Production</c>.</summary>
    public string Environment { get; init; } = string.Empty;

    /// <summary>The version of FluentCMS core.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// The type of the exception that kept the report from being complete, or null. The sections it covers are then empty or
    /// not known; the exception itself is in the log.
    /// </summary>
    public string? Failure { get; init; }

    /// <summary>The database provider in use.</summary>
    public StartupDataProvider DataProvider { get; init; } = new();

    /// <summary>The FluentCMS assemblies loaded in the process.</summary>
    public IReadOnlyList<StartupAssembly> Assemblies { get; init; } = [];

    /// <summary>The plugins the host runs, in plugin order.</summary>
    public IReadOnlyList<StartupPlugin> Plugins { get; init; } = [];

    /// <summary>The provider slots and the implementation each one resolves to.</summary>
    public IReadOnlyList<StartupProviderSlot> ProviderSlots { get; init; } = [];

    /// <summary>The migrations of the system, plugin and content streams that are not applied yet.</summary>
    public IReadOnlyList<StartupMigration> PendingMigrations { get; init; } = [];

    /// <summary>The schema in code set against the one last applied to the database.</summary>
    public StartupSchema Schema { get; init; } = new();

    /// <summary>Settings that are fine on a developer machine and not on a server.</summary>
    public IReadOnlyList<InsecureDefault> InsecureDefaults { get; init; } = [];
}
