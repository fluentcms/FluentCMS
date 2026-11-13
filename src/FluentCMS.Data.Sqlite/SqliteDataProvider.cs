using System.Linq.Expressions;
using System.Reflection;
using FluentCMS.Core.Plugins;
using FluentCMS.Core.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SQLitePCL;

namespace FluentCMS.Data.Sqlite;

// It fills the database file slot of core, IDatabaseFile, which `fluentcms backup` and `fluentcms restore` use.
public sealed class SqliteDataProvider(string connectionString, bool writeAheadLog = true) : IDataProvider, IDatabaseFile
{
    // The write interceptors keep writers from starving in the rollback journal, which a host may keep with writeAheadLog false.
    private static readonly IInterceptor[] WriteInterceptors = [new SqliteWriteInterceptor(), new SqliteCommandTransactions()];

    private readonly IInterceptor[] _interceptors = writeAheadLog ? [new SqliteWriteAheadLog(), .. WriteInterceptors] : WriteInterceptors;

    private readonly SqliteDatabaseFile _file = new(connectionString, writeAheadLog);

    public string Name => "SQLite";

    string? IDatabaseFile.FilePath => _file.FilePath;

    public bool MigrateOnStartup => true;

    public bool SupportsSchemaSync => true;

    public IStorageConventions Conventions { get; } = new SqliteStorageConventions();

    public IJsonSqlDialect JsonDialect { get; } = new SqliteJsonDialect();

    public IJobClaimDialect JobClaimDialect { get; } = new SqliteJobClaimDialect();

    public IDatabaseSchemaReader SchemaReader { get; } = new SqliteSchemaReader();

    // A primary key counts, as it does on the other providers.
    public bool IsUniqueViolation(Exception exception) =>
        (exception as SqliteException ?? exception.InnerException as SqliteException) is { SqliteExtendedErrorCode: raw.SQLITE_CONSTRAINT_UNIQUE or raw.SQLITE_CONSTRAINT_PRIMARYKEY };

    // A writer takes the lock at BEGIN IMMEDIATE and holds it to the end, so writers queue and never deadlock.
    public bool IsDeadlock(Exception exception) => false;

    // SQLite's LIKE folds the case of ASCII letters only.
    public Expression<Func<string?, bool>> ContainsIgnoringCase(string fragment)
    {
        var pattern = LikePattern.Containing(fragment);
        return text => EF.Functions.Like(text, pattern, LikePattern.Escape);
    }

    public void ConfigureContentMigrations(DbContextOptionsBuilder options, Assembly migrationsAssembly) =>
        options.UseSqlite(connectionString, sqlite => sqlite
            .MigrationsAssembly(migrationsAssembly)
            .MigrationsHistoryTable(CmsDbContext.HistoryTable))
            .AddInterceptors(_interceptors);

    public void ConfigurePluginMigrations(DbContextOptionsBuilder options, Assembly migrationsAssembly, string historyTable) =>
        options.UseSqlite(connectionString, sqlite => sqlite
            .MigrationsAssembly(migrationsAssembly)
            .MigrationsHistoryTable(historyTable))
            .AddInterceptors(_interceptors)
            .UsePluginMigrationSet(PluginMigrationSets.Sqlite);

    public void ConfigureSystemMigrations(DbContextOptionsBuilder options) =>
        options.UseSqlite(connectionString, sqlite => sqlite
            .MigrationsAssembly(typeof(SqliteDataProvider).Assembly)
            .MigrationsHistoryTable(SystemDbContext.HistoryTable))
            .AddInterceptors(_interceptors);

    void IDatabaseFile.Snapshot(string destination) => _file.Snapshot(destination);

    bool IDatabaseFile.IsInUse() => _file.IsInUse();

    void IDatabaseFile.Restore(string snapshot) => _file.Restore(snapshot);
}
