using System.Data;
using System.Linq.Expressions;
using System.Reflection;
using FluentCMS.Core.Plugins;
using FluentCMS.Core.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
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
