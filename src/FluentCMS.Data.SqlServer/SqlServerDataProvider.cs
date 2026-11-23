using System.Data;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using FluentCMS.Core.Plugins;
using FluentCMS.Core.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace FluentCMS.Data.SqlServer;

public sealed class SqlServerDataProvider(string connectionString) : IDataProvider
{
    public string Name => "SQL Server";

    public bool MigrateOnStartup => false;

    public bool SupportsSchemaSync => false;

    public IStorageConventions Conventions { get; } = new SqlServerStorageConventions();

    public IJsonSqlDialect JsonDialect { get; } = new SqlServerJsonDialect();

    public IJobClaimDialect JobClaimDialect { get; } = new SqlServerJobClaimDialect();

    public IDatabaseSchemaReader SchemaReader { get; } = new InformationSchemaReader(SqlServerCatalog.CurrentSchema, SqlServerCatalog.Indexes, SqlServerCatalog.CheckConstraints);

    // 2601 is a unique index, 2627 a unique or primary key constraint.
    public bool IsUniqueViolation(Exception exception) =>
        (exception as SqlException ?? exception.InnerException as SqlException) is { Number: 2601 or 2627 };

    // 547 is any conflict with a constraint, a CHECK such as the ISJSON of a JSON column included, so the kind the message
    // names is read too: REFERENCE for a delete a foreign key refuses, FOREIGN KEY for an insert or an update. A server whose
    // messages word these otherwise has its refusal taken for another failure, which is the safe side.
    public bool IsForeignKeyViolation(Exception exception) =>
        (exception as SqlException ?? exception.InnerException as SqlException) is { Number: 547, Message: var message }
        && (message.Contains("REFERENCE", StringComparison.Ordinal) || message.Contains("FOREIGN KEY", StringComparison.Ordinal));

    // EF's execution strategy wraps a deadlock met in SaveChanges as a transient failure, so the whole chain is read.
    public bool IsDeadlock(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: 1205 })
            {
                return true;
            }
        }

        return false;
    }

    // Writers wait for the rows they write, not for the whole database.
    public bool IsBusy(Exception exception) => false;

    public bool HasOneWriter => false;

    // EF's migrations lock is an application lock of the session, which ends with it.
    public Task<string?> ReadMigrationsLockAsync(DbContext context, CancellationToken cancellationToken) => Task.FromResult<string?>(null);

    // A locking read, which READ_COMMITTED_SNAPSHOT leaves to the hints: shared locks do not block each other, and the
    // exclusive one waits for them all. A lock that waited for a deletion finds no row.
    public async Task<bool> LockSiteAsync(DbContext context, Guid site, bool exclusive, CancellationToken cancellationToken)
    {
        var rows = exclusive
            ? await context.Database.SqlQuery<int>($"SELECT 1 AS [Value] FROM cms_site WITH (XLOCK, ROWLOCK, HOLDLOCK) WHERE id = {site}").ToListAsync(cancellationToken)
            : await context.Database.SqlQuery<int>($"SELECT 1 AS [Value] FROM cms_site WITH (REPEATABLEREAD, ROWLOCK) WHERE id = {site}").ToListAsync(cancellationToken);
        return rows.Count > 0;
    }

    // SNAPSHOT isolation, where the database allows it. The migrations turn on READ_COMMITTED_SNAPSHOT, whose reads are each
    // a snapshot of their own statement; a whole transaction needs ALLOW_SNAPSHOT_ISOLATION, which an administrator turns on.
    public async Task<IDbContextTransaction?> BeginSnapshotAsync(DbContext context, CancellationToken cancellationToken)
    {
        var state = await context.Database
            .SqlQueryRaw<byte>("SELECT snapshot_isolation_state AS [Value] FROM sys.databases WHERE database_id = DB_ID()")
            .ToListAsync(cancellationToken);
        return state is [1] ? await context.Database.BeginTransactionAsync(IsolationLevel.Snapshot, cancellationToken) : null;
    }

    // EF's sp_getapplock has no lock timeout, so its wait for another session ends at the command timeout with an error that
    // names nothing (#3192). An application lock of the session is taken again by the same session at once, so EF's own
    // request then succeeds, and its release leaves this one held until the handle releases it.
    public async Task<IAsyncDisposable?> TakeMigrationsLockAsync(DbContext context, CancellationToken cancellationToken)
    {
        if (!await context.GetService<IRelationalDatabaseCreator>().ExistsAsync(cancellationToken))
        {
            return new UncreatedDatabase(context);
        }

        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var result = await RunAsync(
                context,
                "DECLARE @result int; EXEC @result = sp_getapplock @Resource = '__EFMigrationsLock', @LockOwner = 'Session', @LockMode = 'Exclusive', @LockTimeout = -1; SELECT @result",
                NoTimeout,
                cancellationToken);
            return result >= 0 ? new MigrationsLock(context) : throw new InvalidOperationException($"sp_getapplock answered {result} for __EFMigrationsLock.");
        }
        catch
        {
            // The server may have granted the lock before the failure or cancellation, so the session is ended, not pooled.
            ClearPool(context);
            await context.Database.CloseConnectionAsync();
            throw;
        }
    }

    // EF takes a share of its own, which it leaves behind when it cannot create its history table, and when its acquire
    // fails it releases the runner's instead, so the release counts on neither and frees what the session holds.
    private const string ReleaseEveryShare = """
        DECLARE @result int = 0;
        WHILE @result >= 0 AND APPLOCK_MODE('public', '__EFMigrationsLock', 'Session') <> 'NoLock'
            EXEC @result = sp_releaseapplock @Resource = '__EFMigrationsLock', @LockOwner = 'Session';
        SELECT @result;
        """;

    // The runner bounds the wait for the lock.
    private const int NoTimeout = 0;

    private const int ReleaseTimeoutSeconds = 5;

    private static void ClearPool(DbContext context) => SqlConnection.ClearPool((SqlConnection)context.Database.GetDbConnection());

    private static async Task<int> RunAsync(DbContext context, string sql, int timeoutSeconds, CancellationToken cancellationToken)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
#pragma warning disable CA2100 // Constants of this class.
        command.CommandText = sql;
#pragma warning restore CA2100
        command.CommandTimeout = timeoutSeconds;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    // A pooled connection keeps its session, and with it the lock, until it is used again, so the lock is released first. A
    // connection that broke has lost its session, and the lock with it, and a session whose release failed is ended.
    private sealed class MigrationsLock(DbContext context) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (context.Database.GetDbConnection().State == ConnectionState.Open)
                {
                    var result = await RunAsync(context, ReleaseEveryShare, ReleaseTimeoutSeconds, CancellationToken.None);
                    if (result < 0)
                    {
                        throw new InvalidOperationException($"sp_releaseapplock answered {result} for __EFMigrationsLock.");
                    }
                }
            }
            catch
            {
                ClearPool(context);
                throw;
            }
            finally
            {
                await context.Database.CloseConnectionAsync();
            }
        }
    }

    // EF creates the database and takes its lock on a connection it then pools, with the share if it failed to release it.
    private sealed class UncreatedDatabase(DbContext context) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            ClearPool(context);
            return ValueTask.CompletedTask;
        }
    }

    // Under a case-insensitive collation of its own, so that a database whose default compares case does not change it.
    public Expression<Func<string?, bool>> ContainsIgnoringCase(string fragment)
    {
        var pattern = LikePattern.Containing(fragment);
        return text => EF.Functions.Like(EF.Functions.Collate(text, SqlServerStorageConventions.CaseInsensitiveCollation), pattern, LikePattern.Escape);
    }

    public void ConfigureContentMigrations(DbContextOptionsBuilder options, Assembly migrationsAssembly) =>
        options.UseSqlServer(connectionString, sqlServer => sqlServer
            .MigrationsAssembly(migrationsAssembly)
            .MigrationsHistoryTable(CmsDbContext.HistoryTable))
            .AddInterceptors(EndedTransactionGuard.Instance);

    public void ConfigurePluginMigrations(DbContextOptionsBuilder options, Assembly migrationsAssembly, string historyTable) =>
        options.UseSqlServer(connectionString, sqlServer => sqlServer
            .MigrationsAssembly(migrationsAssembly)
            .MigrationsHistoryTable(historyTable))
            .UsePluginMigrationSet(PluginMigrationSets.SqlServer)
            .AddInterceptors(EndedTransactionGuard.Instance);

    public void ConfigureSystemMigrations(DbContextOptionsBuilder options) =>
        options.UseSqlServer(connectionString, sqlServer => sqlServer
            .MigrationsAssembly(typeof(SqlServerDataProvider).Assembly)
            .MigrationsHistoryTable(SystemDbContext.HistoryTable))
            .AddInterceptors(EndedTransactionGuard.Instance);
}
