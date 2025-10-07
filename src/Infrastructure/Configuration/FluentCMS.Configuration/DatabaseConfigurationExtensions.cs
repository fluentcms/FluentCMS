using FluentCMS.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FluentCMS.Configuration;

/// <summary>
/// Extension methods for adding database configuration
/// </summary>
public static class DatabaseConfigurationExtensions
{
    /// <summary>
    /// Adds database configuration provider to the configuration builder.
    /// Automatically discovers all sections registered via AddDatabaseOptions.
    /// </summary>
    /// <param name="builder">Configuration builder</param>
    /// <param name="dbOptions">Database context options</param>
    /// <param name="reloadInterval">Optional reload interval for automatic updates</param>
    public static IConfigurationBuilder AddDatabaseConfiguration(
        this IConfigurationBuilder builder,
        DbContextOptions<ConfigurationDbContext> dbOptions,
        TimeSpan? reloadInterval = null)
    {
        // Build current configuration to access appsettings.json
        var tempConfig = builder.Build();

        // Get all registered sections
        var registeredSections = DatabaseConfigurationRegistry.GetRegisteredSections()
            .Select(kvp => kvp.Key)
            .ToList();

        return builder.Add(new DatabaseConfigurationSource
        {
            DbOptions = dbOptions,
            ReloadInterval = reloadInterval ?? TimeSpan.Zero,
            SeedConfiguration = tempConfig,
            DynamicSections = registeredSections
        });
    }

    /// <summary>
    /// Adds database configuration provider with SQLite.
    /// Automatically discovers all sections registered via AddDatabaseOptions.
    /// </summary>
    /// <param name="builder">Configuration builder</param>
    /// <param name="connectionString">SQLite connection string</param>
    /// <param name="reloadInterval">Optional reload interval for automatic updates</param>
    public static IConfigurationBuilder AddDatabaseConfiguration(
        this IConfigurationBuilder builder,
        string connectionString,
        TimeSpan? reloadInterval = null)
    {
        var dbOptions = new DbContextOptionsBuilder<ConfigurationDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return builder.AddDatabaseConfiguration(dbOptions, reloadInterval);
    }
}
