namespace FluentCMS.Entities;

/// <summary>
/// Represents content specifically associated with a plugin.
/// Inherits from <see cref="Content"/> to leverage common content properties and behaviors.
/// </summary>
public class PluginContent : Content
{
    /// <summary>
    /// Identifier of the associated plugin.
    /// </summary>
    public Guid PluginId { get; set; }
}
