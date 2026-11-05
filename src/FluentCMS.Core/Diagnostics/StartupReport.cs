using System.Text.Json;

namespace FluentCMS.Core.Diagnostics;

// What the host knows about itself once it has started. It holds no connection string, key or password: the values it
// names are choices and versions, and the free text in it has been through SecretScrubber.
internal sealed record StartupReport(
    DateTimeOffset StartedAt,
    string Environment,
    string Version,
    StartupDataProvider DataProvider,
    IReadOnlyList<StartupAssembly> Assemblies,
    IReadOnlyList<StartupPlugin> Plugins,
    IReadOnlyList<StartupMigration> PendingMigrations,
    StartupSchema Schema,
    IReadOnlyList<InsecureDefault> InsecureDefaults)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}

// Provider is the name the data provider gives itself, such as SQLite.
internal sealed record StartupDataProvider(string Provider, string ProviderVersion, bool MigrateOnStartup, string SchemaSync);

internal sealed record StartupAssembly(string Name, string Version);

internal sealed record StartupPlugin(string Name, string Version);

// Stream is system, content or the name of a plugin.
internal sealed record StartupMigration(string Stream, string Id);

// Hash is the schema in code, AppliedHash the one the content tables were last brought up to, null while the history is
// empty. Changes says what differs when they do.
internal sealed record StartupSchema(string Hash, string? AppliedHash, DateTimeOffset? AppliedAt, string? AppliedVersion, bool Drifted, string? Changes);

// A setting that is fine on a developer machine and not on a server. Id is the FCMS code of the production check that
// refuses it, or a short name for a default that is only reported.
internal sealed record InsecureDefault(string Id, string Message);
