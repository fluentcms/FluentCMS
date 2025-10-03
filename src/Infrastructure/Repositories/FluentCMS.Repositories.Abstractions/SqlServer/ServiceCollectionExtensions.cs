using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.EntityFramework;

namespace FluentCMS.Repositories.SqlServer;

// Extension methods for configuring SqlServer database connections
public static class SqlServerServiceCollectionExtensions
{
    // Add UseSqlServer method to the connection builder
    public static void UseSqlServer(this IDatabaseConnectionBuilder builder, string connectionString)
    {
        // This extension is available when the SqlServer package is referenced
        // It provides the implementation for UseSqlServer()
        builder.SetFactory(() => new EfDataContext(EfDbContextConfiguration.CreateDbContextOptions("SqlServer", connectionString)));
    }
}
