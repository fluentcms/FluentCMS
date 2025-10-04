using FluentCMS.Repositories.Abstractions.Configuration;

namespace FluentCMS.Repositories.Sqlite;

/// <summary>
/// Extension methods for configuring SQLite database provider in the database manager.
/// This package provides SQLite support for the FluentCMS repository system.
/// </summary>
public static class SqliteDatabaseExtensions
{
    /// <summary>
    /// Configures this area to use SQLite database provider.
    /// Requires the FluentCMS.Repositories.Sqlite package.
    /// </summary>
    /// <param name="configuration">The database area configuration</param>
    /// <param name="connectionString">The SQLite connection string</param>
    /// <returns>This configuration instance for chaining</returns>
    /// <example>
    /// <code>
    /// services.AddDatabaseManager(options =>
    /// {
    ///     options.Default()
    ///         .UseSqlite("Data Source=myapp.db")
    ///         .EnableDataSeeding(/* ... */);
    /// });
    /// </code>
    /// </example>
    public static DatabaseAreaConfiguration UseSqlite(
        this DatabaseAreaConfiguration configuration,
        string connectionString)
    {
        configuration.DatabaseProvider = new SqliteDatabaseProvider();
        configuration.ConnectionString = connectionString;
        return configuration;
    }
}
