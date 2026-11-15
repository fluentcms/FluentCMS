using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Text.Json;
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

    // Schema sync's record. Sync moves no value, so for a host that schema apply serves, a schema that loses data by the
    // newest row keeps that row, which tells schema apply what is lost, and only schema apply records past it. A host with
    // content migrations has nothing that would record past it, so it records as before. Returns whether it recorded.
    public async Task<bool> RecordAfterSyncAsync(SchemaModel schema, Func<CancellationToken, Task<bool>> usesSchemaApply, CancellationToken cancellationToken)
    {
        try
        {
            if (await usesSchemaApply(cancellationToken))
            {
                var (report, unreadable) = Compare(await AppliedSnapshotAsync(cancellationToken), schema);
                if (Losses(report, unreadable, schema).Count > 0)
                {
                    return false;
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SchemaHistoryLog.NotRecorded(logger, exception.GetType().Name, exception);
            return false;
        }

        await RecordAsync(schema, cancellationToken);
        return true;
    }

    // From the newest row's snapshot to a schema, which tells a renamed field and a changed kind that the tables alone do
    // not. Null while the history is empty; unreadable when its snapshot is not in the form of a schema.
    public static (SchemaDiffReport? Report, bool Unreadable) Compare(string? applied, SchemaModel schema)
    {
        if (applied is null)
        {
            return (null, false);
        }

        try
        {
            return (SchemaSnapshotDiff.Report(applied, SchemaSnapshot.Of(schema).Json), false);
        }
        catch (JsonException)
        {
            return (null, true);
        }
    }

    // What the history says is lost although the tables keep it: a removed field or type, a changed kind, and a renamed
    // field whose values are kept in JSON under its former name. A snapshot that cannot be read counts, since renames
    // cannot be checked.
    public static List<string> Losses(SchemaDiffReport? report, bool unreadable, SchemaModel schema)
    {
        List<string> losses = [];
        if (report is not null)
        {
            losses.AddRange(report.Destructive.Select(change => change.ToString()));
            losses.AddRange(ContentRenames.Of(schema, report.Changes).InJson
                .Select(change => $"{change}, whose values are kept in JSON under '{change.FormerField}', which schema apply does not rename"));
        }

        if (unreadable)
        {
            losses.Add("the snapshot in the newest row of the schema history is not in the form of a schema, so renamed fields and changed kinds cannot be checked");
        }

        return losses;
    }

    // Logs what differs between the code and the newest row. It never fails startup: a schema that drifted is the
    // operator's to apply, and a missing check must not stop a host that works. A host that schema apply serves is asked to
    // run it; usesSchemaApply is asked only when there is something to log.
    public async Task CheckDriftAsync(SchemaModel schema, Func<CancellationToken, Task<bool>> usesSchemaApply, CancellationToken cancellationToken)
    {
        try
        {
            var status = await StatusAsync(schema, cancellationToken);
            if (status.AppliedHash is null)
            {
                SchemaHistoryLog.NoHistory(logger, await usesSchemaApply(cancellationToken) ? "run 'fluentcms schema apply' to record it" : $"run 'fluentcms migrate' to record it, or start with {DatabaseOptions.MigrateOnStartupKey} = true");
            }
            else if (status.Drifted)
            {
                SchemaHistoryLog.Drift(
                    logger,
                    status.AppliedAt!.Value,
                    status.AppliedVersion!,
                    status.AppliedHash,
                    status.Hash,
                    status.Changes!,
                    await usesSchemaApply(cancellationToken) ? "Run 'fluentcms schema apply' to apply and record it" : $"Run 'fluentcms migrate' to apply and record it, or start with {DatabaseOptions.MigrateOnStartupKey} = true");
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

    [LoggerMessage(EventId = 9314, Level = LogLevel.Information, Message = "The schema history is empty, so the schema in code cannot be compared; {Fix}.")]
    public static partial void NoHistory(ILogger logger, string fix);

    [LoggerMessage(EventId = 9315, Level = LogLevel.Warning, Message = "The schema in code differs from the one applied on {AppliedAt:u} by FluentCMS {Version} (hash {Applied}, code {Current}): {Changes}. {Fix}.")]
    public static partial void Drift(ILogger logger, DateTimeOffset appliedAt, string version, string applied, string current, string changes, string fix);
}
