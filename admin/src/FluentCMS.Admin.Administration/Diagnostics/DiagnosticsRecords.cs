using System.Text.Json;
using System.Text.Json.Serialization;
using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Administration.Diagnostics;

// The report the host made once it started (row 10.14), as GET /system/diagnostics answers it. It holds no connection
// string, key or password. Failure names the type of what kept it from being complete, whose sections are then empty.
internal sealed record StartupReportRecord
{
    public DateTimeOffset StartedAt { get; init; }

    public string Environment { get; init; } = "";

    public string Version { get; init; } = "";

    public string? Failure { get; init; }

    public StartupDataProviderRecord DataProvider { get; init; } = new();

    public IReadOnlyList<StartupNamedVersion> Assemblies { get; init; } = [];

    public IReadOnlyList<StartupNamedVersion> Plugins { get; init; } = [];

    public IReadOnlyList<StartupSlotRecord> ProviderSlots { get; init; } = [];

    public IReadOnlyList<StartupMigrationRecord> PendingMigrations { get; init; } = [];

    public StartupSchemaRecord Schema { get; init; } = new();

    public IReadOnlyList<InsecureDefaultRecord> InsecureDefaults { get; init; } = [];
}

internal sealed record StartupDataProviderRecord
{
    public string Provider { get; init; } = "";

    public string ProviderVersion { get; init; } = "";

    public bool MigrateOnStartup { get; init; }

    public string SchemaSync { get; init; } = "";
}

// An assembly of FluentCMS in the process, or a plugin the host runs.
internal sealed record StartupNamedVersion
{
    public string Name { get; init; } = "";

    public string Version { get; init; } = "";
}

internal sealed record StartupSlotRecord
{
    public string Slot { get; init; } = "";

    public string? Implementation { get; init; }

    public string Source { get; init; } = "";

    public string? Plugin { get; init; }
}

internal sealed record StartupMigrationRecord
{
    public string Stream { get; init; } = "";

    public string Id { get; init; } = "";
}

// The schema in code set against the one last applied to the database.
internal sealed record StartupSchemaRecord
{
    public string Hash { get; init; } = "";

    public string? AppliedHash { get; init; }

    public DateTimeOffset? AppliedAt { get; init; }

    public string? AppliedVersion { get; init; }

    public bool Drifted { get; init; }

    public string? Changes { get; init; }
}

// A setting that is fine on a developer machine and not on a server.
internal sealed record InsecureDefaultRecord
{
    public string Id { get; init; } = "";

    public string Message { get; init; } = "";
}

// Every check of the host, live and ready, as GET /system/health answers it, which is 200 whatever the status.
internal sealed record HealthRecord
{
    public required string Status { get; init; }

    public double TotalDurationMs { get; init; }

    public IReadOnlyList<HealthCheckRecord> Checks { get; init; } = [];
}

internal sealed record HealthCheckRecord
{
    public required string Name { get; init; }

    public required string Status { get; init; }

    public double DurationMs { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    public string? Description { get; init; }

    public string? Error { get; init; }
}

// The calls of the diagnostics area (spec 4.15). Both need system.read for the instance, and neither is about content.
internal static class DiagnosticsApi
{
    public static async Task<StartupReportRecord> GetStartupReportAsync(this IManagementApi api, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        return (await api.SendAsync(new ApiRequest(HttpMethod.Get, "system/diagnostics") { NoLocale = true }, DiagnosticsJsonContext.Default.StartupReportRecord, cancellationToken)).Value;
    }

    public static async Task<HealthRecord> GetHealthAsync(this IManagementApi api, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        return (await api.SendAsync(new ApiRequest(HttpMethod.Get, "system/health") { NoLocale = true }, DiagnosticsJsonContext.Default.HealthRecord, cancellationToken)).Value;
    }

    public static bool IsFailure(Exception exception) =>
        exception is ApiProblemException or HttpRequestException or TaskCanceledException or JsonException;

    // The report is made once the host has started, and answers 503 until then.
    public static bool NotMadeYet(Exception exception) => exception is ApiProblemException { Problem.Status: 503 };
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(StartupReportRecord))]
[JsonSerializable(typeof(HealthRecord))]
internal sealed partial class DiagnosticsJsonContext : JsonSerializerContext;
