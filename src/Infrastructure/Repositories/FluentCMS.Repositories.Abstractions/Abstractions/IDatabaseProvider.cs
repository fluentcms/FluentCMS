using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Repositories.Abstractions;

/// <summary>
/// Defines a contract for database providers to configure DbContext options
/// and handle provider-specific setup.
/// </summary>
public interface IDatabaseProvider
{
    /// <summary>
    /// The database provider name/type (e.g., "Sqlite", "SqlServer", "MySql")
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Configures the DbContext options for this database provider.
    /// </summary>
    /// <param name="optionsBuilder">The options builder to configure</param>
    /// <param name="connectionString">The connection string for the database</param>
    void Configure(DbContextOptionsBuilder optionsBuilder, string connectionString);
}
