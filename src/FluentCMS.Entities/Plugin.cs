namespace FluentCMS.Entities;

/// <summary>
/// Represents a plugin associated with a page on a site.
/// Inherits from <see cref="SiteAssociatedEntity"/> for site association.
/// Includes properties for defining its relationship with pages and its placement.
/// </summary>
public class Plugin : SiteAssociatedEntity
{
    /// <summary>
    /// Identifier of the plugin definition.
    /// </summary>
    public Guid DefinitionId { get; set; }

    /// <summary>
    /// Identifier of the page where the plugin is placed.
    /// </summary>
    public Guid PageId { get; set; }

    /// <summary>
    /// Order of the plugin on the page.
    /// </summary>
    public int Order { get; set; } = 0;

    /// <summary>
    /// The section of the page where the plugin is located.
    /// </summary>
    public string Section { get; set; } = default!;
}
