using FluentCMS.Repositories.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Repositories.EntityFramework.Configuration;

/// <summary>
/// Represents the configuration for a database connection including provider and connection settings
/// </summary>
public class DatabaseConfiguration
{
    /// <summary>
    /// Action to configure DbContextOptions with the appropriate database provider and settings
    /// </summary>
    public Action<DbContextOptionsBuilder> ConfigureOptions { get; set; } = null!;

    /// <summary>
    /// Data seeding configuration for this database
    /// Null if seeding is not configured (seeding will not run)
    /// </summary>
    public DataSeedingOptions? SeedingOptions { get; set; }

    /// <summary>
    /// Databse schema validation configuration for this database
    /// Null if schema validation is not configured (schema validation will not run)
    /// </summary>
    public SchemaValidatorOptions? SchemaValidatorOptions { get; set; }

    /// <summary>
    /// The connection string for the database
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// The marker type used to identify this configuration (null for default configuration)
    /// </summary>
    public Type? MarkerType { get; set; }

    /// <summary>
    /// Applies the configuration to a DbContextOptionsBuilder
    /// </summary>
    /// <param name="builder">The DbContextOptionsBuilder to configure</param>
    internal void Apply(DbContextOptionsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        ConfigureOptions?.Invoke(builder);
    }
}
