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
}
