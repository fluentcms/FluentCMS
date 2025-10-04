using FluentCMS.Repositories.Abstractions.Configuration;

namespace FluentCMS.Repositories.SqlServer;

/// <summary>
/// Extension methods for configuring SQL Server database provider in the database manager.
/// This package provides SQL Server support for the FluentCMS repository system.
/// </summary>
public static class SqlServerDatabaseExtensions
{
    /// <summary>
    /// Configures this area to use SQL Server database provider.
    /// Requires the FluentCMS.Repositories.SqlServer package.
    /// </summary>
    /// <param name="configuration">The database area configuration</param>
    /// <param name="connectionString">The SQL Server connection string</param>
    /// <returns>This configuration instance for chaining</returns>
    /// <example>
    /// <code>
    /// services.AddDatabaseManager(options =>
    /// {
    ///     options.For<ITodoDatabaseMarker>()
    ///         .UseSqlServer("Server=localhost;Database=TodoDb;Trusted_Connection=True;")
    ///         .EnableDataSeeding(/* ... */);
    /// });
    /// </code>
    /// </example>
    public static DatabaseAreaConfiguration UseSqlServer(
        this DatabaseAreaConfiguration configuration,
        string connectionString)
    {
        configuration.DatabaseProvider = new SqlServerDatabaseProvider();
        configuration.ConnectionString = connectionString;
        return configuration;

        // TODO: Advanced options support when extracted to separate package
        // public static DatabaseAreaConfiguration UseSqlServer(
        //     this DatabaseAreaConfiguration configuration,
        //     string connectionString,
        //     Action<SqlServerDbContextOptionsBuilder>? sqlServerOptionsAction = null)
    }
}
