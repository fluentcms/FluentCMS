using System.Reflection;
using System.Text.Json;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Storage;

// Reads and writes cms_schema_history. The newest row is the schema the content tables were last brought up to.
internal sealed class SchemaHistory(IServiceScopeFactory scopes, TimeProvider time, ILogger logger)
{
    private const int VersionLength = 64;

    private static readonly string Version = ResolveVersion();

    // Adds a row unless the newest one already has the hash of the schema.
    public async Task RecordAsync(SchemaModel schema, CancellationToken cancellationToken)
    {
        var snapshot = SchemaSnapshot.Of(schema);

        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        var last = await NewestAsync(context, cancellationToken);
        if (last?.Hash == snapshot.Hash)
        {
            return;
        }

        context.Set<SchemaHistoryRow>().Add(new SchemaHistoryRow
        {
            Hash = snapshot.Hash,
            Snapshot = snapshot.Json,
            AppliedAt = time.GetUtcNow(),
            FluentcmsVersion = Version,
        });
        await context.SaveChangesAsync(cancellationToken);

        SchemaHistoryLog.Recorded(logger, snapshot.Hash, last is null ? "first schema" : Describe(last.Snapshot, snapshot.Json));
    }

    // Logs what differs between the code and the newest row. It never fails startup: a schema that drifted is the
    // operator's to apply, and a missing check must not stop a host that works.
    public async Task CheckDriftAsync(SchemaModel schema, CancellationToken cancellationToken)
    {
        var snapshot = SchemaSnapshot.Of(schema);

        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        var last = await NewestAsync(context, cancellationToken);
        if (last is null)
        {
            SchemaHistoryLog.NoHistory(logger);
        }
        else if (last.Hash != snapshot.Hash)
        {
            SchemaHistoryLog.Drift(logger, last.AppliedAt, last.FluentcmsVersion, last.Hash, snapshot.Hash, Describe(last.Snapshot, snapshot.Json));
        }
    }

    private static Task<SchemaHistoryRow?> NewestAsync(SystemDbContext context, CancellationToken cancellationToken) =>
        context.Set<SchemaHistoryRow>()
            .AsNoTracking()
            .OrderByDescending(row => row.AppliedAt)
            .ThenByDescending(row => row.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private static string Describe(string before, string after)
    {
        try
        {
            var changes = SchemaSnapshotDiff.Compare(before, after);
            return changes.Count == 0 ? "no change to a type or field, only to the order of types or the format of the snapshot" : string.Join("; ", changes);
        }
        catch (JsonException)
        {
            return "the stored snapshot cannot be read";
        }
    }

    private static string ResolveVersion()
    {
        var assembly = typeof(SchemaHistory).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";

        var metadata = version.IndexOf('+', StringComparison.Ordinal);
        version = metadata < 0 ? version : version[..metadata];
        return version.Length <= VersionLength ? version : version[..VersionLength];
    }
}

internal static partial class SchemaHistoryLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Recorded schema {Hash} in the schema history: {Changes}.")]
    public static partial void Recorded(ILogger logger, string hash, string changes);

    [LoggerMessage(Level = LogLevel.Information, Message = "The schema history is empty, so the schema in code cannot be compared; 'fluentcms migrate' records it.")]
    public static partial void NoHistory(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The schema in code differs from the one applied on {AppliedAt:u} by FluentCMS {Version} (hash {Applied}, code {Current}): {Changes}. 'fluentcms migrate' applies and records it.")]
    public static partial void Drift(ILogger logger, DateTimeOffset appliedAt, string version, string applied, string current, string changes);
}
