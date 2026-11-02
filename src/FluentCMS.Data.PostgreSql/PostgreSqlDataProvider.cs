using FluentCMS.Core.Storage;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Data.PostgreSql;

public sealed class PostgreSqlDataProvider(string connectionString) : IDataProvider
{
    public string Name => "PostgreSQL";

    public bool MigrateOnStartup => false;

    public IStorageConventions Conventions { get; } = new PostgreSqlStorageConventions();

    public IJsonSqlDialect JsonDialect { get; } = new PostgreSqlJsonDialect();

    public IJobClaimDialect JobClaimDialect { get; } = new PostgreSqlJobClaimDialect();

    public void ConfigureDbContext(DbContextOptionsBuilder options) => options.UseNpgsql(connectionString);

    public void ConfigureSystemMigrations(DbContextOptionsBuilder options) =>
        options.UseNpgsql(connectionString, npgsql => npgsql
            .MigrationsAssembly(typeof(PostgreSqlDataProvider).Assembly)
            .MigrationsHistoryTable(SystemDbContext.HistoryTable));
}
