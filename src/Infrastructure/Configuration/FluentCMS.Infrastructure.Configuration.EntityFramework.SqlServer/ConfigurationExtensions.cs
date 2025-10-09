using FluentCMS.Infrastructure.Configuration.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FluentCMS.Configuration.EntityFramework.SqlServer;

/// <summary>
/// Extension methods for adding database configuration with SQL Server
/// </summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// Adds database configuration provider with SQL Server.
    /// Automatically discovers all sections registered via AddDatabaseOptions.
    /// </summary>
    /// <param name="builder">Configuration builder</param>
    /// <param name="connectionString">SQL Server connection string</param>
    /// <param name="reloadInterval">Optional reload interval for automatic updates</param>
    public static IConfigurationBuilder AddSqlServerConfiguration(this IConfigurationBuilder builder, string connectionString, TimeSpan? reloadInterval = null)
    {

        // This creates ONLY configuration, no DI container
        var tempConfig = DatabaseConfigurationExtensions.CreateTemporaryConfigurationManager();

        var dbOptions = new DbContextOptionsBuilder<ConfigurationDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return builder.AddDatabaseConfiguration(dbOptions, reloadInterval);
    }
}
