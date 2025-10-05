using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Repositories;

public class DataMigrationOptions
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
