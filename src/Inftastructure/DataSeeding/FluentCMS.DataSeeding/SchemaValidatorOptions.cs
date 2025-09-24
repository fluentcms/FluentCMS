using FluentCMS.DataSeeding.Abstractions;

namespace FluentCMS.DataSeeding;

/// <summary>
/// Configuration options for the schema validation process
/// </summary>
public class SchemaValidatorOptions
{
    /// <summary>
    /// List of conditions that must be met before seeding occurs
    /// </summary>
    public List<ICondition> Conditions { get; set; } = [];

    /// <summary>
    /// Whether to ignore exceptions during the seeding process
    /// </summary>
    public bool IgnoreExceptions { get; set; } = false;

    /// <summary>
    /// Timeout for schema validation operations. Default is 5 minutes.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);
}
