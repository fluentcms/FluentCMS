using FluentCMS.Core.Storage;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Data.Sqlite;

public sealed class SqliteDataProvider(string connectionString) : IDataProvider
{
    public string Name => "SQLite";

    public bool MigrateOnStartup => true;

    public IStorageConventions Conventions { get; } = new SqliteStorageConventions();

    public IJsonSqlDialect JsonDialect { get; } = new SqliteJsonDialect();

    public IJobClaimDialect JobClaimDialect { get; } = new SqliteJobClaimDialect();

    public void ConfigureDbContext(DbContextOptionsBuilder options) => options.UseSqlite(connectionString);

    public void ConfigureSystemMigrations(DbContextOptionsBuilder options) =>
        options.UseSqlite(connectionString, sqlite => sqlite
            .MigrationsAssembly(typeof(SqliteDataProvider).Assembly)
            .MigrationsHistoryTable(SystemDbContext.HistoryTable));
}
