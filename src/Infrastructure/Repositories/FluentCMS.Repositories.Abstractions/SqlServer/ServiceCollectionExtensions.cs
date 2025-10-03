using Microsoft.EntityFrameworkCore;
using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.EntityFramework;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace FluentCMS.Repositories.SqlServer;

// Extension methods for configuring SqlServer database connections
public static class SqlServerServiceCollectionExtensions
{
    // Add UseSqlServer method to the connection builder
    public static void UseSqlServer(this IDatabaseConnectionBuilder builder, string connectionString, Action<SqlServerDbContextOptionsBuilder>? optionsAction = null)
    {
        // This extension is available when the SqlServer package is referenced
        builder.SetFactory(() =>
        {
            var contextOptionsBuilder = new DbContextOptionsBuilder();
            var sqlServerBuilder = contextOptionsBuilder.UseSqlServer(connectionString, optionsAction);
            optionsAction?.Invoke(new SqlServerDbContextOptionsBuilder(sqlServerBuilder));
            return new EfDataContext(contextOptionsBuilder.Options);
        });
    }
}
