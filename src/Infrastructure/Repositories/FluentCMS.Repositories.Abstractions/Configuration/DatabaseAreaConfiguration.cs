namespace FluentCMS.Repositories.Abstractions.Configuration;

/// <summary>
/// Configuration for a specific database area, including provider settings and data seeding options.
/// Provider-specific configuration methods are added via extension methods in separate packages.
/// </summary>
public class DatabaseAreaConfiguration
{
    /// <summary>
    /// The database provider for this area.
    /// </summary>
    public IDatabaseProvider? DatabaseProvider { get; internal set; }

    /// <summary>
    /// The connection string for the database.
    /// </summary>
    public string? ConnectionString { get; internal set; }

    /// <summary>
    /// The data seeding configuration for this area.
    /// </summary>
    public DataSeedingOptions DataSeedingOptions { get; } = new();

    /// <summary>
    /// Enables data seeding for this area with the specified configuration.
    /// </summary>
    /// <param name="configureSeeding">Action to configure data seeding options</param>
    /// <returns>This configuration instance for chaining</returns>
    public DatabaseAreaConfiguration EnableDataSeeding(Action<DataSeedingOptions>? configureSeeding = null)
    {
        configureSeeding?.Invoke(DataSeedingOptions);
        return this;
    }
}
