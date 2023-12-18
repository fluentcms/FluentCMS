using FluentCMS.Entities;

namespace FluentCMS.Api.Models;

/// <summary>
/// Represents a response model for a plugin entity in the FluentCMS system.
/// </summary>
public class PluginResponse
{
    /// <summary>
    /// Gets or sets the unique identifier of the plugin.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the definition of the plugin <see cref="PluginDefinition"/>.
    /// </summary>
    public PluginDefinition Definition { get; set; } = default!;

    /// <summary>
    /// Gets or sets the order in which the plugin appears relative to others in the same section.
    /// </summary>
    public int Order { get; set; } = 0;

    /// <summary>
    /// Gets or sets the section of the page where the plugin is located.
    /// </summary>
    public string Section { get; set; } = default!;
}
