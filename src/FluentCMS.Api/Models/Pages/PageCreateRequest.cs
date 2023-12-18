namespace FluentCMS.Api.Models;

/// <summary>
/// Represents a request for creating a new page in the FluentCMS system.
/// </summary>
public class PageCreateRequest
{
    /// <summary>
    /// Gets or sets the unique identifier of the site to which the page will belong.
    /// </summary>
    public Guid SiteId { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier of the parent page, if any. Null if the page is a top-level page.
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// Gets or sets the title of the page.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URL path of the page.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the order in which the page appears relative to its siblings.
    /// </summary>
    public int Order { get; set; }
}
