using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Repositories.Abstractions;

/// <summary>
/// Base interface for database area configurations
/// </summary>
public interface IDatabaseAreaConfiguration
{
    /// <summary>
    /// The database provider for this configuration
    /// </summary>
    IDatabaseProvider? DatabaseProvider { get; }

    /// <summary>
    /// The connection string for this configuration
    /// </summary>
    string? ConnectionString { get; }

    /// <summary>
    /// Provider-specific options configuration
    /// </summary>
    Action<DbContextOptionsBuilder>? ProviderOptions { get; }

    /// <summary>
    /// Data seeding configuration
    /// </summary>
    DataSeedingOptions? SeedingOptions { get; }

    /// <summary>
    /// Enable data seeding for this area
    /// </summary>
    /// <param name="seedingConfiguration">Configuration for data seeding</param>
    /// <returns>The configuration for chaining</returns>
    IDatabaseAreaConfiguration EnableDataSeeding(Action<DataSeedingOptions> seedingConfiguration);
}
