using System.Reflection;
using FluentCMS.Core.Storage;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Data.Sqlite;

public sealed class SqliteDataProvider(string connectionString) : IDataProvider
{
    public string Name => "SQLite";

    public bool MigrateOnStartup => true;

    public bool SupportsSchemaSync => true;

    public IStorageConventions Conventions { get; } = new SqliteStorageConventions();

    public IJsonSqlDialect JsonDialect { get; } = new SqliteJsonDialect();

    public IJobClaimDialect JobClaimDialect { get; } = new SqliteJobClaimDialect();

    public IDatabaseSchemaReader SchemaReader { get; } = new SqliteSchemaReader();

    public void ConfigureContentMigrations(DbContextOptionsBuilder options, Assembly migrationsAssembly) =>
        options.UseSqlite(connectionString, sqlite => sqlite
            .MigrationsAssembly(migrationsAssembly)
            .MigrationsHistoryTable(CmsDbContext.HistoryTable));

    public void ConfigureSystemMigrations(DbContextOptionsBuilder options) =>
        options.UseSqlite(connectionString, sqlite => sqlite
            .MigrationsAssembly(typeof(SqliteDataProvider).Assembly)
            .MigrationsHistoryTable(SystemDbContext.HistoryTable));
}
