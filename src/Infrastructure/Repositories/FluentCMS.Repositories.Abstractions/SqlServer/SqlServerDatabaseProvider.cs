using Microsoft.EntityFrameworkCore;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Repositories.SqlServer;

/// <summary>
/// Database provider implementation for SQL Server databases.
/// Uses Microsoft SQL Server as the database engine.
/// Part of the FluentCMS.Repositories.SqlServer package.
/// </summary>
internal class SqlServerDatabaseProvider : IDatabaseProvider
{
    /// <summary>
    /// Gets the name of this database provider.
    /// </summary>
    public string ProviderName => "SqlServer";

    /// <summary>
    /// Configures the DbContext options for SQL Server database.
    /// </summary>
    /// <param name="optionsBuilder">The options builder to configure</param>
    /// <param name="connectionString">The connection string for the SQL Server database</param>
    public void Configure(DbContextOptionsBuilder optionsBuilder, string connectionString)
    {
        optionsBuilder.UseSqlServer(connectionString);
    }
}
