using System.Reflection;
using FluentCMS.Core.Plugins;
using FluentCMS.Core.Storage;
using Microsoft.EntityFrameworkCore;
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

    public IDatabaseSchemaReader SchemaReader { get; } = new InformationSchemaReader("current_schema()");

    public bool IsUniqueViolation(Exception exception) =>
        (exception as PostgresException ?? exception.InnerException as PostgresException) is { SqlState: PostgresErrorCodes.UniqueViolation };

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
            await RunAsync(connection, $"SELECT pg_advisory_lock({MigrationsLockKey})", cancellationToken);
            return new MigrationsLock(connection);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private const string MigrationsLockKey = "hashtext(current_database() || ':fluentcms:migrations')";

    private static async Task RunAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Constants of this class.
        command.CommandText = sql;
#pragma warning restore CA2100
        command.CommandTimeout = 0;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // A pooled connection keeps its session, and with it the lock, until it is used again, so the lock is released first.
    private sealed class MigrationsLock(NpgsqlConnection connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await RunAsync(connection, $"SELECT pg_advisory_unlock({MigrationsLockKey})", CancellationToken.None);
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
