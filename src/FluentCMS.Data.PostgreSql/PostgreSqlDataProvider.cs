using System.Data;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using FluentCMS.Core.Plugins;
using FluentCMS.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace FluentCMS.Data.PostgreSql;

public sealed class PostgreSqlDataProvider(string connectionString) : IDataProvider
{
    public string Name => "PostgreSQL";

    public bool MigrateOnStartup => false;

    public bool SupportsSchemaSync => false;

    public IStorageConventions Conventions { get; } = new PostgreSqlStorageConventions();

    public IJsonSqlDialect JsonDialect { get; } = new PostgreSqlJsonDialect();

    public IJobClaimDialect JobClaimDialect { get; } = new PostgreSqlJobClaimDialect();

    public IDatabaseSchemaReader SchemaReader { get; } = new InformationSchemaReader(PostgreSqlCatalog.CurrentSchema, PostgreSqlCatalog.Indexes);

    public bool IsUniqueViolation(Exception exception) =>
        (exception as PostgresException ?? exception.InnerException as PostgresException) is { SqlState: PostgresErrorCodes.UniqueViolation };

    // The error names the constraint or the unique index in a field of its own.
    public bool IsUniqueViolation(Exception exception, IUniqueConstraint key) =>
        (exception as PostgresException ?? exception.InnerException as PostgresException) is { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: { } name }
        && string.Equals(name, key?.Name, StringComparison.Ordinal);

    public bool IsForeignKeyViolation(Exception exception) =>
        (exception as PostgresException ?? exception.InnerException as PostgresException) is { SqlState: PostgresErrorCodes.ForeignKeyViolation };

    // EF's execution strategy wraps a deadlock met in SaveChanges as a transient failure, so the whole chain is read.
    public bool IsDeadlock(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected })
            {
                return true;
            }
        }

        return false;
    }

    // Writers wait for the rows they write, not for the whole database.
    public bool IsBusy(Exception exception) => false;

    public bool HasOneWriter => false;

    // SET LOCAL ends with the transaction, committed or rolled back, so a pooled session keeps nothing of it.
    public async Task<IDbContextTransaction> BeginWriteAsync(DbContext context, TimeSpan lockTimeout, CancellationToken cancellationToken)
    {
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await context.Database.ExecuteSqlRawAsync(string.Create(CultureInfo.InvariantCulture, $"SET LOCAL lock_timeout = {Milliseconds(lockTimeout)}"), cancellationToken);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    // 55P03 lock_not_available, which lock_timeout raises, read in the whole chain as a deadlock is.
    public bool IsLockTimeout(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable })
            {
                return true;
            }
        }

        return false;
    }

    private static long Milliseconds(TimeSpan timeout) => Math.Max(1, (long)Math.Ceiling(timeout.TotalMilliseconds));

    // Npgsql's migrations lock is a lock of the history table, which ends with the transaction that took it.
    public Task<string?> ReadMigrationsLockAsync(DbContext context, CancellationToken cancellationToken) => Task.FromResult<string?>(null);

    // FOR KEY SHARE does not block another writer's, and FOR UPDATE waits for every one; a lock that waited for a deletion
    // finds no row.
    public async Task<bool> LockSiteAsync(DbContext context, Guid site, bool exclusive, CancellationToken cancellationToken)
    {
        var rows = exclusive
            ? await context.Database.SqlQuery<int>($"""SELECT 1 AS "Value" FROM cms_site WHERE id = {site} FOR UPDATE""").ToListAsync(cancellationToken)
            : await context.Database.SqlQuery<int>($"""SELECT 1 AS "Value" FROM cms_site WHERE id = {site} FOR KEY SHARE""").ToListAsync(cancellationToken);
        return rows.Count > 0;
    }

    // A locking read, where an update that sets nothing new writes a new row version all the same (#3575). FOR NO KEY UPDATE
    // is the lock that update takes, so the same statements wait for it and a wait past lock_timeout or in a deadlock fails
    // as the update's did; a read that waited for an update or a deletion reads the row as it then is.
    public async Task<bool> HoldDocumentAsync(DbContext context, Guid id, int? draftVersion, CancellationToken cancellationToken)
    {
        var rows = draftVersion is { } version
            ? await context.Database.SqlQuery<int>($"""SELECT 1 AS "Value" FROM cms_document WHERE id = {id} AND draft_version = {version} AND deleted_at IS NULL FOR NO KEY UPDATE""").ToListAsync(cancellationToken)
            : await context.Database.SqlQuery<int>($"""SELECT 1 AS "Value" FROM cms_document WHERE id = {id} FOR NO KEY UPDATE""").ToListAsync(cancellationToken);
        return rows.Count > 0;
    }

    // REPEATABLE READ is PostgreSQL's snapshot isolation: every read sees the snapshot of the first, and no writer waits for it.
    public async Task<IDbContextTransaction?> BeginSnapshotAsync(DbContext context, CancellationToken cancellationToken) =>
        await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);

    public Expression<Func<string?, bool>> ContainsIgnoringCase(string fragment)
    {
        var pattern = LikePattern.Containing(fragment);
        return text => EF.Functions.ILike(text!, pattern, LikePattern.Escape);
    }

    public void ConfigureContentMigrations(DbContextOptionsBuilder options, Assembly migrationsAssembly) =>
        options.UseNpgsql(connectionString, npgsql => npgsql
            .MigrationsAssembly(migrationsAssembly)
            .MigrationsHistoryTable(CmsDbContext.HistoryTable));

    public void ConfigurePluginMigrations(DbContextOptionsBuilder options, Assembly migrationsAssembly, string historyTable) =>
        options.UseNpgsql(connectionString, npgsql => npgsql
            .MigrationsAssembly(migrationsAssembly)
            .MigrationsHistoryTable(historyTable))
            .UsePluginMigrationSet(PluginMigrationSets.PostgreSql);

    public void ConfigureSystemMigrations(DbContextOptionsBuilder options) =>
        options.UseNpgsql(connectionString, npgsql => npgsql
            .MigrationsAssembly(typeof(PostgreSqlDataProvider).Assembly)
            .MigrationsHistoryTable(SystemDbContext.HistoryTable));

    // Npgsql locks the history table inside each migration's transaction, after the migrator has read what is applied, so
    // two processes that start together both apply the next migration. A session advisory lock, keyed by the database
    // since the keys are shared by the whole server, holds the other process until this one has applied every stream.
    public async Task<IAsyncDisposable?> LockMigrationsAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await RunAsync(connection, $"SELECT pg_advisory_lock({MigrationsLockKey})", NoTimeout, cancellationToken);
            return new MigrationsLock(connection);
        }
        catch
        {
            // The server may have granted the lock before the failure or cancellation, so the session is ended, not pooled.
            NpgsqlConnection.ClearPool(connection);
            await connection.DisposeAsync();
            throw;
        }
    }

    private const string MigrationsLockKey = "hashtext(current_database() || ':fluentcms:migrations')";

    private const int NoTimeout = 0;

    private const int ReleaseTimeoutSeconds = 5;

    private static async Task RunAsync(NpgsqlConnection connection, string sql, int timeoutSeconds, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Constants of this class.
        command.CommandText = sql;
#pragma warning restore CA2100
        command.CommandTimeout = timeoutSeconds;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // A pooled connection keeps its session, and with it the lock, until it is used again, so the lock is released first, and
    // a session whose release failed is ended.
    private sealed class MigrationsLock(NpgsqlConnection connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await RunAsync(connection, $"SELECT pg_advisory_unlock({MigrationsLockKey})", ReleaseTimeoutSeconds, CancellationToken.None);
            }
            catch
            {
                NpgsqlConnection.ClearPool(connection);
                throw;
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
