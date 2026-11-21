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

    // EF's sp_getapplock has no lock timeout, so its wait for another session ends at the command timeout with an error that
    // names nothing (#3192). An application lock of the session is taken again by the same session at once, so EF's own
    // request then succeeds, and its release leaves this one held until the handle releases it.
    public async Task<IAsyncDisposable?> TakeMigrationsLockAsync(DbContext context, CancellationToken cancellationToken)
    {
        if (!await context.GetService<IRelationalDatabaseCreator>().ExistsAsync(cancellationToken))
        {
            return null;
        }

        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var result = await RunAsync(
                context,
                "DECLARE @result int; EXEC @result = sp_getapplock @Resource = '__EFMigrationsLock', @LockOwner = 'Session', @LockMode = 'Exclusive', @LockTimeout = -1; SELECT @result",
                cancellationToken);
            return result >= 0 ? new MigrationsLock(context) : throw new InvalidOperationException($"sp_getapplock answered {result} for __EFMigrationsLock.");
        }
        catch
        {
            await context.Database.CloseConnectionAsync();
            throw;
        }
    }

    // With no command timeout: the runner bounds the wait.
    private static async Task<int> RunAsync(DbContext context, string sql, CancellationToken cancellationToken)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
#pragma warning disable CA2100 // Constants of this class.
        command.CommandText = sql;
#pragma warning restore CA2100
        command.CommandTimeout = 0;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    // A pooled connection keeps its session, and with it the lock, until it is used again, so the lock is released first. A
    // connection that broke has lost its session, and the lock with it.
    private sealed class MigrationsLock(DbContext context) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (context.Database.GetDbConnection().State == ConnectionState.Open)
                {
                    await RunAsync(context, "DECLARE @result int; EXEC @result = sp_releaseapplock @Resource = '__EFMigrationsLock', @LockOwner = 'Session'; SELECT @result", CancellationToken.None);
                }
            }
            finally
            {
                await context.Database.CloseConnectionAsync();
            }
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
