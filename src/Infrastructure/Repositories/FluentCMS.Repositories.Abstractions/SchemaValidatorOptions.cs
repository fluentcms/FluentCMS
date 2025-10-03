using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Repositories;

/// <summary>
/// Configuration options for the schema validation process
/// </summary>
public class SchemaValidatorOptions
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

