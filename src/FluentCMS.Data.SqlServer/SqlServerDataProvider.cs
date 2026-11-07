using System.Reflection;
using FluentCMS.Core.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Data.SqlServer;

public sealed class SqlServerDataProvider(string connectionString, bool nativeJson = false) : IDataProvider
{
    public string Name => "SQL Server";

    public bool MigrateOnStartup => false;

    public bool SupportsSchemaSync => false;

    public IStorageConventions Conventions { get; } = new SqlServerStorageConventions(nativeJson);

    public IJsonSqlDialect JsonDialect { get; } = new SqlServerJsonDialect();

    public IJobClaimDialect JobClaimDialect { get; } = new SqlServerJobClaimDialect();

    public IDatabaseSchemaReader SchemaReader { get; } = new InformationSchemaReader("SCHEMA_NAME()");

    // 2601 is a unique index, 2627 a unique or primary key constraint.
    public bool IsUniqueViolation(Exception exception) =>
        (exception as SqlException ?? exception.InnerException as SqlException) is { Number: 2601 or 2627 };

    public void ConfigureContentMigrations(DbContextOptionsBuilder options, Assembly migrationsAssembly) =>
        options.UseSqlServer(connectionString, sqlServer => sqlServer
            .MigrationsAssembly(migrationsAssembly)
            .MigrationsHistoryTable(CmsDbContext.HistoryTable));

    public void ConfigurePluginMigrations(DbContextOptionsBuilder options, Assembly migrationsAssembly, string historyTable) =>
        options.UseSqlServer(connectionString, sqlServer => sqlServer
            .MigrationsAssembly(migrationsAssembly)
            .MigrationsHistoryTable(historyTable));

    public void ConfigureSystemMigrations(DbContextOptionsBuilder options) =>
        options.UseSqlServer(connectionString, sqlServer => sqlServer
            .MigrationsAssembly(typeof(SqlServerDataProvider).Assembly)
            .MigrationsHistoryTable(SystemDbContext.HistoryTable));
}
