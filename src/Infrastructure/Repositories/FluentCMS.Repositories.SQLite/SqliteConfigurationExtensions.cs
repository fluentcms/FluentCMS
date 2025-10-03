using FluentCMS.Repositories.EntityFramework.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace FluentCMS.Repositories.Sqlite;
/// <summary>
/// Extension methods for configuring SQLite database provider
/// </summary>
public static class SqliteConfigurationExtensions
{
    /// <summary>
    /// Configures the database to use SQLite with the specified connection string
    /// </summary>
    /// <param name="builder">The database configuration builder</param>
    /// <param name="connectionString">The SQLite connection string</param>
    /// <param name="sqliteOptionsAction">Optional action to configure SQLite-specific options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The configuration builder for chaining</returns>
    public static IDatabaseConfigurationBuilder UseSqlite(
        this IDatabaseConfigurationBuilder builder,
        string connectionString,
        Action<SqliteDbContextOptionsBuilder>? sqliteOptionsAction = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // Store the connection string in the configuration
        builder.Configuration.ConnectionString = connectionString;

        // Configure the DbContextOptions to use SQLite
        builder.Configuration.ConfigureOptions = options =>
        {
            options.UseSqlite(connectionString, sqliteOptionsAction);
        };

        return builder;
    }
}
