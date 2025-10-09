using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FluentCMS.Infrastructure.Configuration.EntityFramework.Sqlite;

/// <summary>
/// Extension methods for adding database configuration with SQLite
/// </summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// Adds database configuration provider with SQLite.
    /// Automatically discovers all sections registered via AddDatabaseOptions.
    /// </summary>
    /// <param name="builder">Configuration builder</param>
    /// <param name="connectionStringName">Connection string name in configuration section</param>
    /// <param name="reloadInterval">Optional reload interval for automatic updates</param>
    public static IConfigurationBuilder AddSqliteConfiguration(this IConfigurationBuilder builder, string connectionStringName, TimeSpan? reloadInterval = null)
    {
        // This creates ONLY configuration, no DI container
        var tempConfig = DatabaseConfigurationExtensions.CreateTemporaryConfigurationManager();

        // Retrieve the connection string from the temporary configuration
        var connectionString = tempConfig.GetConnectionString(connectionStringName) ??
            throw new InvalidOperationException($"Connection string {connectionStringName} not found.");

        var dbOptions = new DbContextOptionsBuilder<ConfigurationDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return builder.AddDatabaseConfiguration(dbOptions, reloadInterval);
    }
}
