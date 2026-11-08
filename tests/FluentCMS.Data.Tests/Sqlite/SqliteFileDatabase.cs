using System.Data.Common;
using System.Reflection;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Storage;
using FluentCMS.Data.Sqlite;
using FluentCMS.Data.Tests.Providers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FluentCMS.Data.Tests.Sqlite;

// A database file of its own, reached through the real package. Pooling is off so that the file can be deleted.
// writeAheadLog false is the provider of a host that keeps the rollback journal.
internal sealed class SqliteFileDatabase(bool writeAheadLog = true) : TestDatabase
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fluentcms-data-tests-");

    public override string ConnectionString => $"Data Source={Path.Combine(_directory.FullName, "cms.db")};Pooling=False";

    public override void UseProvider(FluentCmsBuilder cms) => cms.UseDataProvider(new UnflushedSqliteProvider(new SqliteDataProvider(ConnectionString, writeAheadLog)));

    public override void UseDriver(DbContextOptionsBuilder options, Assembly migrationsAssembly, string historyTable) =>
        options.UseSqlite(ConnectionString, sqlite => sqlite.MigrationsAssembly(migrationsAssembly).MigrationsHistoryTable(historyTable));

    // From the model, which is quicker than the migrations; SystemMigrationTests applies those.
    public override void CreateSystemTables(SystemDbContext context) => context.Database.EnsureCreated();

    public override void UseJournalMode(string journalMode) => Execute($"PRAGMA journal_mode = {journalMode}");

    public override List<string> Tables() =>
        ReadStrings("select name from sqlite_master where type = 'table' and name not like 'sqlite_%' order by name");

    public override List<string> Columns(string table) =>
        ReadStrings($"select name from pragma_table_info('{table}') order by name");

    public override void Dispose() => _directory.Delete(recursive: true);

    protected override DbConnection NewConnection() => new SqliteConnection(ConnectionString);

    // A commit of a file database waits for the disk to flush, and the tests seed rows and create tables one commit at a time, which is most of what they cost on a Windows runner. The locks behave the same without it, and the file is deleted after the test.
    private sealed class UnflushedSqliteProvider(SqliteDataProvider inner) : IDataProvider
    {
        private static readonly NoFlush Interceptor = new();

        public string Name => inner.Name;

        public bool MigrateOnStartup => inner.MigrateOnStartup;

        public bool SupportsSchemaSync => inner.SupportsSchemaSync;

        public IStorageConventions Conventions => inner.Conventions;

        public IJsonSqlDialect JsonDialect => inner.JsonDialect;

        public IJobClaimDialect JobClaimDialect => inner.JobClaimDialect;

        public IDatabaseSchemaReader SchemaReader => inner.SchemaReader;

        public bool IsUniqueViolation(Exception exception) => inner.IsUniqueViolation(exception);

        public void ConfigureContentMigrations(DbContextOptionsBuilder options, Assembly migrationsAssembly)
        {
            inner.ConfigureContentMigrations(options, migrationsAssembly);
            options.AddInterceptors(Interceptor);
        }

        public void ConfigureSystemMigrations(DbContextOptionsBuilder options)
        {
            inner.ConfigureSystemMigrations(options);
            options.AddInterceptors(Interceptor);
        }

        public void ConfigurePluginMigrations(DbContextOptionsBuilder options, Assembly migrationsAssembly, string historyTable)
        {
            inner.ConfigurePluginMigrations(options, migrationsAssembly, historyTable);
            options.AddInterceptors(Interceptor);
        }
    }

    private sealed class NoFlush : DbConnectionInterceptor
    {
        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) => Off(connection);

        public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Off(connection);
            return Task.CompletedTask;
        }

        private static void Off(DbConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA synchronous = OFF";
            command.ExecuteNonQuery();
        }
    }
}
