using System.Reflection;
using FluentCMS.Core.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SQLitePCL;

namespace FluentCMS.Data.Sqlite;

public sealed class SqliteDataProvider(string connectionString, bool writeAheadLog = true) : IDataProvider
{
    // The write interceptors keep writers from starving in the rollback journal, which a host may keep with writeAheadLog false.
    private static readonly IInterceptor[] WriteInterceptors = [new SqliteWriteInterceptor(), new SqliteCommandTransactions()];

    private readonly IInterceptor[] _interceptors = writeAheadLog ? [new SqliteWriteAheadLog(), .. WriteInterceptors] : WriteInterceptors;

    public string Name => "SQLite";

    public bool MigrateOnStartup => true;

    public bool SupportsSchemaSync => true;

    public IStorageConventions Conventions { get; } = new SqliteStorageConventions();

    public IJsonSqlDialect JsonDialect { get; } = new SqliteJsonDialect();

    public IJobClaimDialect JobClaimDialect { get; } = new SqliteJobClaimDialect();

    public IDatabaseSchemaReader SchemaReader { get; } = new SqliteSchemaReader();

    // A primary key counts, as it does on the other providers.
    public bool IsUniqueViolation(Exception exception) =>
        (exception as SqliteException ?? exception.InnerException as SqliteException) is { SqliteExtendedErrorCode: raw.SQLITE_CONSTRAINT_UNIQUE or raw.SQLITE_CONSTRAINT_PRIMARYKEY };

    public void ConfigureContentMigrations(DbContextOptionsBuilder options, Assembly migrationsAssembly) =>
        options.UseSqlite(connectionString, sqlite => sqlite
            .MigrationsAssembly(migrationsAssembly)
            .MigrationsHistoryTable(CmsDbContext.HistoryTable))
            .AddInterceptors(_interceptors);

    public void ConfigurePluginMigrations(DbContextOptionsBuilder options, Assembly migrationsAssembly, string historyTable) =>
        options.UseSqlite(connectionString, sqlite => sqlite
            .MigrationsAssembly(migrationsAssembly)
            .MigrationsHistoryTable(historyTable))
            .AddInterceptors(_interceptors);

    public void ConfigureSystemMigrations(DbContextOptionsBuilder options) =>
        options.UseSqlite(connectionString, sqlite => sqlite
            .MigrationsAssembly(typeof(SqliteDataProvider).Assembly)
            .MigrationsHistoryTable(SystemDbContext.HistoryTable))
            .AddInterceptors(_interceptors);
}
