using FluentCMS.DataSeeding.Abstractions;

namespace FluentCMS.DataSeeding;

/// <summary>
/// Configuration options for the database seeding process
/// </summary>
public class DataSeederOptions
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
    /// Timeout for seeding operations. Default is 5 minutes.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);
}
