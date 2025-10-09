using FluentCMS.Infrastructure.Repositories.EntityFramework.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace FluentCMS.Infrastructure.Repositories.EntityFramework.SqlServer;

/// <summary>
/// Extension methods for configuring SQL Server database provider
/// </summary>
public static class SqlServerDatabaseConfiguration
{
    /// <summary>
    /// Configures the database to use SQL Server with the specified connection string
    /// </summary>
    /// <param name="builder">The database configuration builder</param>
    /// <param name="connectionString">The SQL Server connection string</param>
    /// <param name="sqlServerOptionsAction">Optional action to configure SQL Server-specific options</param>
    /// <returns>The configuration builder for chaining</returns>
    public static DatabaseConfigurationBuilder UseSqlServer(this DatabaseConfigurationBuilder builder, string connectionString, Action<SqlServerDbContextOptionsBuilder>? sqlServerOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // Store the connection string in the configuration
        builder.Configuration.ConnectionString = connectionString;

        // Configure the DbContextOptions to use SQL Server
        builder.Configuration.ConfigureOptions = options =>
        {
            options.UseSqlServer(connectionString, sqlServerOptionsAction);
        };

        return builder;
    }
}
