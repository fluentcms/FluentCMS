namespace FluentCMS.Configuration;

/// <summary>
/// Represents a configuration entry stored in the database
/// </summary>
public class ConfigurationEntity
{
    /// <summary>
    /// Unique identifier for the configuration entry
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

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

    /// <summary>
    /// Timestamp when the configuration was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the configuration was last updated
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
