namespace FluentCMS.Entities;

/// <summary>
/// Represents the definition of a plugin, detailing its characteristics and functionalities.
/// Inherits from <see cref="AuditEntity"/> for audit tracking.
/// </summary>
public class PluginDefinition : AuditEntity
{
    /// <summary>
    /// Name of the plugin.
    /// </summary>
    public string Name { get; set; } = default!;

    /// <summary>
    /// Optional description of the plugin.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Type of the view component associated with the plugin, if applicable.
    /// </summary>
    public required string ViewType { get; set; }

    /// <summary>
    /// Type of the edit component for the plugin's settings, if applicable.
    /// </summary>
    public string? EditType { get; set; }

    /// <summary>
    /// Type of the settings component for the plugin, if applicable.
    /// </summary>
    public string? SettingType { get; set; }
}
