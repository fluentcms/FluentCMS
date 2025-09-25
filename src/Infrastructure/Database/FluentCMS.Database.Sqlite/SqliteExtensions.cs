using FluentCMS.Database.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Database.Sqlite;

/// <summary>
/// Extension methods for configuring SQLite database provider with library-based markers.
/// </summary>
public static class SqliteExtensions
{
    /// <summary>
    /// Configures the current library mapping to use SQLite database.
    /// </summary>
    /// <param name="builder">The library mapping builder.</param>
    /// <param name="connectionString">The SQLite connection string.</param>
    /// <returns>The library mapping builder for method chaining.</returns>
    public static ILibraryMappingBuilder UseSqlite(this ILibraryMappingBuilder builder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));

        return builder.RegisterProvider("Sqlite", connectionString, (connString, serviceProvider) =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<SqliteDatabaseManager>>();
            return new SqliteDatabaseManager(connString, logger);
        });
    }
}
