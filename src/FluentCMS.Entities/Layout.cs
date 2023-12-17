namespace FluentCMS.Entities;

/// <summary>
/// Represents a layout entity associated with a site.
/// Inherits from <see cref="SiteAssociatedEntity"/>, linking it with a specific site and its related properties.
/// Contains template parts for rendering, such as body and head sections of a webpage layout.
/// </summary>
public class Layout : SiteAssociatedEntity
{
    /// <summary>
    /// Name of the layout.
    /// </summary>
    public string Name { get; set; } = default!;

    /// <summary>
    /// Body content of the layout, typically containing HTML or template syntax.
    /// </summary>
    public string Body { get; set; } = default!;

    /// <summary>
    /// Head content of the layout, typically containing HTML or template syntax for the head section.
    /// </summary>
    public string Head { get; set; } = default!;
}
