using FluentCMS.Database.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Database.SqlServer;

/// <summary>
/// Extension methods for configuring SQL Server database provider with library-based markers.
/// </summary>
public static class SqlServerExtensions
{
    /// <summary>
    /// Configures the current library mapping to use SQL Server database.
    /// </summary>
    /// <param name="builder">The library mapping builder.</param>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <returns>The library mapping builder for method chaining.</returns>
    public static ILibraryMappingBuilder UseSqlServer(this ILibraryMappingBuilder builder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));

        return builder.RegisterProvider("SqlServer", connectionString, (connString, serviceProvider) =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<SqlServerDatabaseManager>>();
            return new SqlServerDatabaseManager(connString, logger);
        });
    }
}
