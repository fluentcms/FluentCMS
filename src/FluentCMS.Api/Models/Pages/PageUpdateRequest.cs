namespace FluentCMS.Api.Models;

/// <summary>
/// Represents a request for updating an existing page in the FluentCMS system.
/// </summary>
public class PageUpdateRequest
{
    /// <summary>
    /// Gets or sets the unique identifier of the page to be updated.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier of the parent page, if any. Null if the page is a top-level page.
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// Gets or sets the title of the page.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Gets or sets the URL path of the page.
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    /// Gets or sets the order in which the page appears relative to its siblings.
    /// </summary>
    public int Order { get; set; }
}
