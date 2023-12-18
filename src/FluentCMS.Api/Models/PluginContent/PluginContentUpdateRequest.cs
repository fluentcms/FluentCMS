namespace FluentCMS.Api.Models;

/// <summary>
/// Represents a request for updating a plugin content entity in the FluentCMS system.
/// This class extends <see cref="ContentUpdateRequest"/> with additional plugin-specific information.
/// </summary>
public class PluginContentUpdateRequest : ContentUpdateRequest
{
    /// <summary>
    /// Gets or sets the unique identifier of the plugin associated with the content.
    /// </summary>
    /// <value>
    /// The Guid representing the unique identifier of the plugin.
    /// </value>
    public Guid PluginId { get; set; }
}
