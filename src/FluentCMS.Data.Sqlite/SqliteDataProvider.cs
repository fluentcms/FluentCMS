using System.Linq.Expressions;
using System.Reflection;
using FluentCMS.Core.Plugins;
using FluentCMS.Core.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using SQLitePCL;

namespace FluentCMS.Data.Sqlite;

// It fills the database file slot of core, IDatabaseFile, which `fluentcms backup` and `fluentcms restore` use.
public sealed class SqliteDataProvider(string connectionString, bool writeAheadLog = true) : IDataProvider, IDatabaseFile
{
    // The write interceptors keep writers from starving in the rollback journal, which a host may keep with writeAheadLog false.
    private static readonly IInterceptor[] WriteInterceptors = [new SqliteWriteInterceptor(), new SqliteCommandTransactions()];

    private readonly IInterceptor[] _interceptors = writeAheadLog ? [new SqliteWriteAheadLog(), .. WriteInterceptors] : WriteInterceptors;

    private readonly SqliteDatabaseFile _file = new(connectionString);

    public string Name => "SQLite";

    string? IDatabaseFile.FilePath => _file.FilePath;

    public bool MigrateOnStartup => true;

    public bool SupportsSchemaSync => true;

    public IStorageConventions Conventions { get; } = new SqliteStorageConventions();

    public IJsonSqlDialect JsonDialect { get; } = new SqliteJsonDialect();

    public IJobClaimDialect JobClaimDialect { get; } = new SqliteJobClaimDialect();

    public IDatabaseSchemaReader SchemaReader { get; } = new SqliteSchemaReader();

    // A primary key counts, as it does on the other providers.
    public bool IsUniqueViolation(Exception exception) =>
        (exception as SqliteException ?? exception.InnerException as SqliteException) is { SqliteExtendedErrorCode: raw.SQLITE_CONSTRAINT_UNIQUE or raw.SQLITE_CONSTRAINT_PRIMARYKEY };

    // SQLite names no key, only its columns, as table.column in the key's order and to the end of its message, and never a
    // value.
    public bool IsUniqueViolation(Exception exception, IUniqueConstraint key) =>
        key is not null
        && (exception as SqliteException ?? exception.InnerException as SqliteException) is { SqliteExtendedErrorCode: raw.SQLITE_CONSTRAINT_UNIQUE or raw.SQLITE_CONSTRAINT_PRIMARYKEY, Message: var message }
        && message.EndsWith($"constraint failed: {string.Join(", ", key.Columns.Select(column => $"{key.Table.Name}.{column.Name}"))}'.", StringComparison.Ordinal);

    public bool IsForeignKeyViolation(Exception exception) =>
        (exception as SqliteException ?? exception.InnerException as SqliteException) is { SqliteExtendedErrorCode: raw.SQLITE_CONSTRAINT_FOREIGNKEY };

    // A writer takes the lock at BEGIN IMMEDIATE and holds it to the end, so writers queue and never deadlock.
    public bool IsDeadlock(Exception exception) => false;

    // What Microsoft.Data.Sqlite throws once the busy timeout and its own retries are over, matched by the primary code.
    public bool IsBusy(Exception exception) =>
        (exception as SqliteException ?? exception.InnerException as SqliteException) is { SqliteErrorCode: raw.SQLITE_BUSY or raw.SQLITE_LOCKED };

    public bool HasOneWriter => true;

    // SQLite locks the database, not a row, so the wait to bound is the one at BEGIN IMMEDIATE for the one write lock, which
    // any other connection's write transaction holds, of whatever entry or table. Microsoft.Data.Sqlite tries a busy statement
    // again until the command timeout, which BEGIN takes from the connection's default, so the default is lowered for it alone,
    // never raised (0 waits without end); once begun the writer waits for no other writer. On a file each try also waits up to
    // the busy timeout inside SQLite (SqliteWriteAheadLog), so the wait ends at the first try past the bound.
    public async Task<IDbContextTransaction> BeginWriteAsync(DbContext context, TimeSpan lockTimeout, CancellationToken cancellationToken)
    {
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        var timeout = connection.DefaultTimeout;
        var bound = (int)Math.Min(int.MaxValue, Math.Max(1, Math.Ceiling(lockTimeout.TotalSeconds)));
        connection.DefaultTimeout = timeout > 0 ? Math.Min(timeout, bound) : bound;
        try
        {
            return await context.Database.BeginTransactionAsync(cancellationToken);
        }
        finally
        {
            connection.DefaultTimeout = timeout;
        }
    }

    public bool IsLockTimeout(Exception exception) => IsBusy(exception);

    // SQLite's LIKE folds the case of ASCII letters only.
    public Expression<Func<string?, bool>> ContainsIgnoringCase(string fragment)
    {
        var pattern = LikePattern.Containing(fragment);
        return text => EF.Functions.Like(text, pattern, LikePattern.Escape);
    }

    public void ConfigureContentMigrations(DbContextOptionsBuilder options, Assembly migrationsAssembly) =>
        options.UseSqlite(connectionString, sqlite => sqlite
            .MigrationsAssembly(migrationsAssembly)
            .MigrationsHistoryTable(CmsDbContext.HistoryTable))
            .AddInterceptors(_interceptors);

    public void ConfigurePluginMigrations(DbContextOptionsBuilder options, Assembly migrationsAssembly, string historyTable) =>
        options.UseSqlite(connectionString, sqlite => sqlite
            .MigrationsAssembly(migrationsAssembly)
            .MigrationsHistoryTable(historyTable))
            .AddInterceptors(_interceptors)
            .UsePluginMigrationSet(PluginMigrationSets.Sqlite);

    public void ConfigureSystemMigrations(DbContextOptionsBuilder options) =>
        options.UseSqlite(connectionString, sqlite => sqlite
            .MigrationsAssembly(typeof(SqliteDataProvider).Assembly)
            .MigrationsHistoryTable(SystemDbContext.HistoryTable))
            .AddInterceptors(_interceptors);

    // EF inserts the row with id 1 to take its lock and deletes it to release it. A database that does not exist yet is left
    // for EF to create, since opening it here would make an empty file that EF then takes for an existing database.
    public async Task<string?> ReadMigrationsLockAsync(DbContext context, CancellationToken cancellationToken)
    {
        if (!await context.GetService<IRelationalDatabaseCreator>().ExistsAsync(cancellationToken))
        {
            return null;
        }

        var tables = await context.Database
            .SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsLock'")
            .ToListAsync(cancellationToken);
        if (tables[0] == 0)
        {
            return null;
        }

        var rows = await context.Database.SqlQueryRaw<string>("SELECT \"Timestamp\" AS \"Value\" FROM \"__EFMigrationsLock\"").ToListAsync(cancellationToken);
        return rows.FirstOrDefault();
    }

    // A deferred transaction reads one snapshot from its first read on, and in WAL mode the writer goes on beside it. In the
    // rollback journal a reader keeps every commit waiting, and a database in memory has no WAL, so neither gets one.
    public async Task<IDbContextTransaction?> BeginSnapshotAsync(DbContext context, CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        using (var mode = connection.CreateCommand())
        {
            mode.CommandText = "PRAGMA journal_mode";
            if (await mode.ExecuteScalarAsync(cancellationToken) is not string journal || !string.Equals(journal, "wal", StringComparison.OrdinalIgnoreCase))
            {
                await context.Database.CloseConnectionAsync();
                return null;
            }
        }

        var transaction = connection.BeginTransaction(deferred: true);
        var used = await context.Database.UseTransactionAsync(transaction, cancellationToken);
        return new SqliteSnapshot(context, used!, transaction);
    }

    void IDatabaseFile.Snapshot(string destination) => _file.Snapshot(destination);

    bool IDatabaseFile.IsInUse() => _file.IsInUse();

    string? IDatabaseFile.Check(string snapshot) => _file.Check(snapshot);

    void IDatabaseFile.Restore(string snapshot) => _file.Restore(snapshot);
}

// EF does not own a transaction it was given, so this ends it and gives the connection back.
internal sealed class SqliteSnapshot(DbContext context, IDbContextTransaction used, SqliteTransaction transaction) : IDbContextTransaction
{
    public Guid TransactionId => used.TransactionId;

    public void Commit() => used.Commit();

    public Task CommitAsync(CancellationToken cancellationToken = default) => used.CommitAsync(cancellationToken);

    public void Rollback() => used.Rollback();

    public Task RollbackAsync(CancellationToken cancellationToken = default) => used.RollbackAsync(cancellationToken);

    public void Dispose()
    {
        used.Dispose();
        transaction.Dispose();
        context.Database.CloseConnection();
    }

    public async ValueTask DisposeAsync()
    {
        await used.DisposeAsync();
        await transaction.DisposeAsync();
        await context.Database.CloseConnectionAsync();
    }
}
