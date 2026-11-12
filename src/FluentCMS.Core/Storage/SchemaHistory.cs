using System.Data;
using System.Data.Common;
using System.Reflection;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Storage;

// Reads and writes cms_schema_history. The row with the highest id is the schema the content tables were last brought up to.
internal sealed class SchemaHistory(IServiceScopeFactory scopes, TimeProvider time, ILogger logger)
{
    private const int VersionLength = 64;
    private const int Attempts = 4;

    private static readonly string Version = ResolveVersion();

    // Adds a row unless the newest one already has the hash of the schema. The read and the insert are one serializable
    // transaction, so nodes that start together add one row between them; where a database settles that by failing one
    // of them (a deadlock, a serialization failure), the loser reads again and finds the row.
    // The migrations that came before stay applied, so a history that cannot be written is logged and never stops the host.
    public async Task RecordAsync(SchemaModel schema, CancellationToken cancellationToken)
    {
        try
        {
            await RecordWithRetriesAsync(SchemaSnapshot.Of(schema), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SchemaHistoryLog.NotRecorded(logger, exception.GetType().Name, exception);
        }
    }

    // The same for a caller that reports a row it could not write.
    public Task RecordOrThrowAsync(SchemaModel schema, CancellationToken cancellationToken) =>
        RecordWithRetriesAsync(SchemaSnapshot.Of(schema), cancellationToken);

    // Logs what differs between the code and the newest row. It never fails startup: a schema that drifted is the
    // operator's to apply, and a missing check must not stop a host that works.
    public async Task CheckDriftAsync(SchemaModel schema, CancellationToken cancellationToken)
    {
        try
        {
            var status = await StatusAsync(schema, cancellationToken);
            if (status.AppliedHash is null)
            {
                SchemaHistoryLog.NoHistory(logger);
            }
            else if (status.Drifted)
            {
                SchemaHistoryLog.Drift(logger, status.AppliedAt!.Value, status.AppliedVersion!, status.AppliedHash, status.Hash, status.Changes!);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SchemaHistoryLog.NotChecked(logger, exception.GetType().Name, exception);
        }
    }

    // The newest row of the history set against the schema in code.
    public async Task<SchemaStatus> StatusAsync(SchemaModel schema, CancellationToken cancellationToken)
    {
        var snapshot = SchemaSnapshot.Of(schema);

        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        var last = await NewestAsync(context, cancellationToken);
        if (last is null)
        {
            return new SchemaStatus(snapshot.Hash, null, null, null, Drifted: false, null);
        }

        var drifted = last.Hash != snapshot.Hash;
        return new SchemaStatus(snapshot.Hash, last.Hash, last.AppliedAt, last.FluentcmsVersion, drifted, drifted ? Describe(last.Snapshot, snapshot.Json) : null);
    }

    // The snapshot of the newest row, or null while the history is empty. A history that cannot be read throws.
    public async Task<string?> AppliedSnapshotAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        return (await NewestAsync(context, cancellationToken))?.Snapshot;
    }

    private async Task RecordWithRetriesAsync(SchemaSnapshot snapshot, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await TryRecordAsync(snapshot, cancellationToken);
                return;
            }
            catch (Exception exception) when (attempt < Attempts && IsDatabaseFailure(exception))
            {
                // Jittered, so that nodes the server failed together do not meet again at the same moment.
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt * (1 + Random.Shared.NextDouble())), cancellationToken);
            }
        }
    }

    // EF's execution strategy on SQL Server and PostgreSQL wraps a deadlock as a transient failure, an InvalidOperationException.
    private static bool IsDatabaseFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException or DbUpdateException)
            {
                return true;
            }
        }

        return false;
    }

    private async Task TryRecordAsync(SchemaSnapshot snapshot, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        string changes;
        await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken))
        {
            var last = await NewestAsync(context, cancellationToken);
            if (last?.Hash == snapshot.Hash)
            {
                return;
            }

            // Before the insert, so that nothing but the commit can fail once the row is there.
            changes = last is null ? "first schema" : Describe(last.Snapshot, snapshot.Json);
            context.Set<SchemaHistoryRow>().Add(new SchemaHistoryRow
            {
                Hash = snapshot.Hash,
                Snapshot = snapshot.Json,
                AppliedAt = time.GetUtcNow(),
                FluentcmsVersion = Version,
            });
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        SchemaHistoryLog.Recorded(logger, snapshot.Hash, changes);
    }

    private static Task<SchemaHistoryRow?> NewestAsync(SystemDbContext context, CancellationToken cancellationToken) =>
        context.Set<SchemaHistoryRow>()
            .AsNoTracking()
            .OrderByDescending(row => row.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private static string Describe(string before, string after)
    {
        try
        {
            var changes = SchemaSnapshotDiff.Compare(before, after);
            return changes.Count == 0 ? "no change to a type or field, only to the order of types or the format of the snapshot" : string.Join("; ", changes);
        }
        catch (Exception)
        {
            return "details are unavailable because the stored snapshot cannot be read";
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

// AppliedHash is null while the history is empty, and Changes when nothing drifted.
internal sealed record SchemaStatus(string Hash, string? AppliedHash, DateTimeOffset? AppliedAt, string? AppliedVersion, bool Drifted, string? Changes);

internal static partial class SchemaHistoryLog
{
    [LoggerMessage(EventId = 9311, Level = LogLevel.Information, Message = "Recorded schema {Hash} in the schema history: {Changes}.")]
    public static partial void Recorded(ILogger logger, string hash, string changes);

    [LoggerMessage(EventId = 9312, Level = LogLevel.Warning, Message = "The schema was not recorded in the schema history ({ExceptionType}); the host starts without it.")]
    public static partial void NotRecorded(ILogger logger, string exceptionType, Exception exception);

    [LoggerMessage(EventId = 9313, Level = LogLevel.Warning, Message = "The schema in code could not be compared with the schema history ({ExceptionType}); the host starts without the check.")]
    public static partial void NotChecked(ILogger logger, string exceptionType, Exception exception);

    [LoggerMessage(EventId = 9314, Level = LogLevel.Information, Message = "The schema history is empty, so the schema in code cannot be compared; run 'fluentcms migrate' to record it, or start with FluentCms:Database:MigrateOnStartup = true.")]
    public static partial void NoHistory(ILogger logger);

    [LoggerMessage(EventId = 9315, Level = LogLevel.Warning, Message = "The schema in code differs from the one applied on {AppliedAt:u} by FluentCMS {Version} (hash {Applied}, code {Current}): {Changes}. Run 'fluentcms migrate' to apply and record it, or start with FluentCms:Database:MigrateOnStartup = true.")]
    public static partial void Drift(ILogger logger, DateTimeOffset appliedAt, string version, string applied, string current, string changes);
}
