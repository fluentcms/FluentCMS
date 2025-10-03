using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.EntityFramework;

namespace FluentCMS.Repositories.Sqlite;

// Extension methods for configuring Sqlite database connections
public static class SqliteServiceCollectionExtensions
{
    // Add UseSqlite method to the connection builder
    public static void UseSqlite(this IDatabaseConnectionBuilder builder, string connectionString)
    {
        // This extension is available when the Sqlite package is referenced
        // It provides the implementation for UseSqlite()
        builder.SetFactory(() => new EfDataContext(EfDbContextConfiguration.CreateDbContextOptions("Sqlite", connectionString)));
    }
}
