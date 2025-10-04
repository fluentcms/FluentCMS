using Microsoft.EntityFrameworkCore;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Repositories.Sqlite;

/// <summary>
/// Database provider implementation for SQLite databases.
/// Uses SQLite as the database engine.
/// Part of the FluentCMS.Repositories.Sqlite package.
/// </summary>
internal class SqliteDatabaseProvider : IDatabaseProvider
{
    /// <summary>
    /// Gets the name of this database provider.
    /// </summary>
    public string ProviderName => "Sqlite";

    /// <summary>
    /// Configures the DbContext options for SQLite database.
    /// </summary>
    /// <param name="optionsBuilder">The options builder to configure</param>
    /// <param name="connectionString">The connection string for the SQLite database</param>
    public void Configure(DbContextOptionsBuilder optionsBuilder, string connectionString)
    {
        optionsBuilder.UseSqlite(connectionString);
    }
}
