using FluentCMS.Core.Storage;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Data.SqlServer;

public sealed class SqlServerDataProvider(string connectionString, bool nativeJson = false) : IDataProvider
{
    public string Name => "SQL Server";

    public bool MigrateOnStartup => false;

    public IStorageConventions Conventions { get; } = new SqlServerStorageConventions(nativeJson);

    public IJsonSqlDialect JsonDialect { get; } = new SqlServerJsonDialect();

    public IJobClaimDialect JobClaimDialect { get; } = new SqlServerJobClaimDialect();

    public void ConfigureDbContext(DbContextOptionsBuilder options) => options.UseSqlServer(connectionString);

    public void ConfigureSystemMigrations(DbContextOptionsBuilder options) =>
        options.UseSqlServer(connectionString, sqlServer => sqlServer
            .MigrationsAssembly(typeof(SqlServerDataProvider).Assembly)
            .MigrationsHistoryTable(SystemDbContext.HistoryTable));
}
