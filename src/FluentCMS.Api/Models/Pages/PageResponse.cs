using FluentCMS.Entities;

namespace FluentCMS.Api.Models;

/// <summary>
/// Represents a response model for a page entity in the FluentCMS system.
/// </summary>
public class PageResponse
{
    /// <summary>
    /// Gets or sets the unique identifier of the page.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier of the parent page, if any.
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier of the site to which the page belongs.
    /// </summary>
    public Guid SiteId { get; set; }

    /// <summary>
    /// Gets or sets the title of the page.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Gets or sets the collection of child pages under this page.
    /// </summary>
    public List<PageResponse> Children { get; set; } = [];

    /// <summary>
    /// Gets or sets the order in which the page appears relative to its siblings.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Gets or sets the URL path of the page.
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    /// Gets or sets the layout associated with the page, if any.
    /// </summary>
    public Layout? Layout { get; set; }

    /// <summary>
    /// Gets or sets the collection of plugins associated with the page.
    /// </summary>
    public List<PluginResponse> Plugins { get; set; } = [];
}
