namespace FluentCMS.Api.Models;

/// <summary>
/// Represents a request for creating a new plugin content entity in the FluentCMS system.
/// This class extends <see cref="ContentCreateRequest"/> with additional plugin-specific information.
/// </summary>
public class PluginContentCreateRequest : ContentCreateRequest
{
    /// <summary>
    /// Gets or sets the unique identifier of the plugin associated with the new content.
    /// </summary>
    /// <value>
    /// The Guid representing the unique identifier of the plugin.
    /// </value>
    public Guid PluginId { get; set; }
}
