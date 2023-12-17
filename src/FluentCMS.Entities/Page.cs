namespace FluentCMS.Entities;

/// <summary>
/// Represents a web page associated with a site.
/// Inherits from <see cref="SiteAssociatedEntity"/> to link it with a specific site.
/// Includes properties for page structure and content.
/// </summary>
public class Page : SiteAssociatedEntity
{
    /// <summary>
    /// Title of the page.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Identifier of the parent page, if applicable.
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// Order of the page among siblings.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Path of the page for URL mapping.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Identifier of the associated layout.
    /// </summary>
    public Guid? LayoutId { get; set; }
}
