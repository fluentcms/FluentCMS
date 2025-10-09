namespace FluentCMS.Infrastructure.Configuration;

/// <summary>
/// Represents a configuration entry stored in the database
/// </summary>
public class ConfigurationEntity : AuditableEntity
{
    /// <summary>
    /// Configuration section name (e.g., "Logging", "ConnectionStrings")
    /// </summary>
    public required string Section { get; set; }

    /// <summary>
    /// JSON serialized value of the configuration
    /// </summary>
    public required string Value { get; set; }

    /// <summary>
    /// Full type name of the configuration object
    /// </summary>
    public required string Type { get; set; }
}
