using System.Data;
using System.Data.Common;
using System.Globalization;
using FluentCMS.Core.Storage;
using FluentCMS.Data.Tests.Providers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace FluentCMS.Data.Tests.Content;

// What the tests of the lock order (#3524) hold a transaction with and look at. Every wait is bounded, so that a deadlock
// or a hang fails its test rather than the run.
internal static class RowLocks
{
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(50);

    public static bool Names(DbCommand command, string value) =>
        command.Parameters.Cast<DbParameter>().Any(parameter => parameter.Value is string text && text == value);

    // Another request, started beside one held in its transaction, and returned once the server shows a session waiting for
    // a lock or the request has ended. SQLite shows nothing of a writer that waits in its BEGIN for the one write lock, so
    // there it is returned at once and waits for the held transaction's commit.
    public static async Task<Task> BesideAsync(string connectionString, Func<Task> request, CancellationToken cancellationToken)
    {
        var running = Task.Run(request, cancellationToken);
        if (TestProvider.IsSqlite)
        {
            return running;
        }

        using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bound.CancelAfter(Limit);
        await using var connection = TestProvider.IsPostgreSql
            ? (DbConnection)new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ToString())
            : new SqlConnection(new SqlConnectionStringBuilder(connectionString) { Pooling = false }.ToString());
        await connection.OpenAsync(bound.Token);
        await using var command = connection.CreateCommand();
        command.CommandText = TestProvider.IsPostgreSql
            ? "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'"
            : "SELECT count(*) FROM sys.dm_tran_locks WHERE resource_database_id = DB_ID() AND request_status <> 'GRANT'";
        try
        {
            while (!running.IsCompleted && Convert.ToInt64(await command.ExecuteScalarAsync(bound.Token), CultureInfo.InvariantCulture) == 0)
            {
                await Task.Delay(Poll, bound.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Waited {Limit.TotalSeconds:0} seconds for the other request to wait for a lock or end.");
        }

        return running;
    }

    // Another request that commits while held is in its transaction, as a database that does not serialise writers lets it
    // while held has written nothing. SQLite has one writer, which held took at its BEGIN, so there held lets it go, having
    // written nothing, and takes it again, with the savepoint it was in.
    public static async Task CommittedMeanwhileAsync(DbContext held, Func<Task> request, string? savepoint = null)
    {
        if (!TestProvider.IsSqlite)
        {
            await request();
            return;
        }

        var transaction = held.Database.CurrentTransaction!.GetDbTransaction();
        await using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "COMMIT";
        await command.ExecuteNonQueryAsync(ServiceDatabase.Token);
        await request();
        command.CommandText = savepoint is null ? "BEGIN IMMEDIATE" : $"BEGIN IMMEDIATE; SAVEPOINT {savepoint}";
        await command.ExecuteNonQueryAsync(ServiceDatabase.Token);
    }

    // What SQLite counts as written on the connection of held since it was opened, which a transaction that has written
    // nothing leaves as it was. Zero on a server, which these tests ask only of SQLite.
    public static async Task<long> ChangesAsync(DbContext held)
    {
        if (!TestProvider.IsSqlite)
        {
            return 0;
        }

        if (held.Database.GetDbConnection().State != ConnectionState.Open)
        {
            await held.Database.OpenConnectionAsync(ServiceDatabase.Token);
        }

        await using var command = held.Database.GetDbConnection().CreateCommand();
        command.Transaction = held.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT total_changes()";
        return Convert.ToInt64(await command.ExecuteScalarAsync(ServiceDatabase.Token), CultureInfo.InvariantCulture);
    }

    // Another request while held is in its transaction: at once while held has written nothing, as above, and beside it once it
    // has, to wait for what it holds. baseline is ChangesAsync of held before its transaction began.
    public static async Task<Task> WhileHeldAsync(DbContext held, long baseline, string connectionString, Func<Task> request, CancellationToken cancellationToken)
    {
        if (TestProvider.IsSqlite && await ChangesAsync(held) == baseline)
        {
            await CommittedMeanwhileAsync(held, request);
            return Task.CompletedTask;
        }

        return await BesideAsync(connectionString, request, cancellationToken);
    }
}

// Runs what a test gives it at the first command of one context that matches, before the command is sent or once it has
// run, and lets the command go on when that is done.
internal sealed class CommandGate(Func<DbCommand, bool> matches, bool after = true) : DbCommandInterceptor
{
    private int _passed;

    public DbContext? Context { get; set; }

    public Func<Task>? At { get; set; }

    public bool Passed => Volatile.Read(ref _passed) == 1;

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (!after)
        {
            await PassAsync(command, eventData.Context);
        }

        return result;
    }

    public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (after)
        {
            await PassAsync(command, eventData.Context);
        }

        return result;
    }

    // A save sends its batch as a query on some providers. Once it has run its reader is open, so it is held only before.
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (!after)
        {
            await PassAsync(command, eventData.Context);
        }

        return result;
    }

    private async Task PassAsync(DbCommand command, DbContext? context)
    {
        if (context is null || !ReferenceEquals(context, Context) || At is not { } at || !matches(command) || Interlocked.Exchange(ref _passed, 1) == 1)
        {
            return;
        }

        await at().WaitAsync(RowLocks.Limit, ServiceDatabase.Token);
    }
}

// Runs what a test gives it before the first transaction of one context begins.
internal sealed class TransactionGate : DbTransactionInterceptor
{
    private int _passed;

    public DbContext? Context { get; set; }

    public Func<Task>? At { get; set; }

    public bool Passed => Volatile.Read(ref _passed) == 1;

    public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context && ReferenceEquals(context, Context) && At is { } at && Interlocked.Exchange(ref _passed, 1) == 0)
        {
            await at().WaitAsync(RowLocks.Limit, ServiceDatabase.Token);
        }

        return result;
    }
}

// Counts the commands that the database ended as a deadlock's victim, which the content service would run again.
internal sealed class Deadlocks : DbCommandInterceptor
{
    private static readonly IDataProvider Provider = TestProvider.Offline();

    private int _count;

    public int Count => Volatile.Read(ref _count);

    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        if (Provider.IsDeadlock(eventData.Exception))
        {
            Interlocked.Increment(ref _count);
        }

        return Task.CompletedTask;
    }
}
