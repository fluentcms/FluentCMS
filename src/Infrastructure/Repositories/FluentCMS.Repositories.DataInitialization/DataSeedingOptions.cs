using FluentCMS.Repositories.DataInitialization.Abstractions;

namespace FluentCMS.Repositories.DataInitialization;

/// <summary>
/// Configuration options for the database seeding process
/// </summary>
public class DataSeedingOptions
{
    /// <summary>
    /// List of conditions that must be met before seeding occurs
    /// </summary>
    public List<IDbInitializationCondition> Conditions { get; set; } = [];

    /// <summary>
    /// Whether to ignore exceptions during the seeding process
    /// </summary>
    public bool IgnoreExceptions { get; set; } = false;
}

public class MigrationOptions
{
    /// <summary>
    /// List of conditions that must be met before migrations are applied
    /// </summary>
    public List<IDbInitializationCondition> Conditions { get; set; } = [];

    /// <summary>
    /// Whether to ignore exceptions during the migration process
    /// </summary>
    public bool IgnoreExceptions { get; set; } = false;
}

