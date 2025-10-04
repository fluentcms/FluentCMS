using Microsoft.EntityFrameworkCore;
using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.EntityFramework;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace FluentCMS.Repositories.Sqlite;

// Extension methods for configuring Sqlite database connections
public static class SqliteServiceCollectionExtensions
{
    // Add UseSqlite method to the connection builder
    public static void UseSqlite(this IDatabaseConnectionBuilder builder, string connectionString, Action<SqliteDbContextOptionsBuilder>? optionsAction = null)
    {
        // This extension is available when the Sqlite package is referenced
        builder.SetFactory(() =>
        {
            var contextOptionsBuilder = new DbContextOptionsBuilder();
            var sqliteBuilder = contextOptionsBuilder.UseSqlite(connectionString);
            optionsAction?.Invoke(new SqliteDbContextOptionsBuilder(contextOptionsBuilder));
            return new EfDataContext(contextOptionsBuilder.Options);
        });
    }
}
