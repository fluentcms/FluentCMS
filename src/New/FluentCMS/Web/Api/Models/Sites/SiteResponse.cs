using FluentCMS.Entities;

namespace FluentCMS.Web.Api.Models;

/// <summary>
/// Represents the response data for a site.
/// </summary>
public class SiteResponse
{
    /// <summary>
    /// Gets or sets the unique identifier of the site.
    /// </summary>
    /// <value>
    /// The unique identifier of the site.
    /// </value>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the user who created the site.
    /// </summary>
    /// <value>
    /// The identifier of the user who created the site.
    /// </value>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the date and time when the site was created.
    /// </summary>
    /// <value>
    /// The creation date and time of the site.
    /// </value>
    public DateTime CreatedAt { get; set; } = default;

    /// <summary>
    /// Gets or sets the identifier of the user who last updated the site.
    /// </summary>
    /// <value>
    /// The identifier of the user who last updated the site.
    /// </value>
    public string LastUpdatedBy { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the date and time when the site was last updated.
    /// </summary>
    /// <value>
    /// The date and time of the last update to the site.
    /// </value>
    public DateTime LastUpdatedAt { get; set; } = default;

    /// <summary>
    /// Gets or sets the name of the site.
    /// </summary>
    /// <value>
    /// The name of the site.
    /// </value>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the description of the site.
    /// </summary>
    /// <value>
    /// A brief description of the site.
    /// </value>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URLs associated with the site.
    /// </summary>
    /// <value>
    /// A list of URLs that are associated with the site.
    /// </value>
    public List<string> Urls { get; set; } = [];

    /// <summary>
    /// Gets or sets the collection of pages within the site.
    /// </summary>
    /// <value>
    /// An enumerable collection of page responses representing the pages within the site.
    /// </value>
    public List<PageResponse> Pages { get; set; } = [];

    /// <summary>
    /// Gets or sets the layout associated with the site.
    /// </summary>
    /// <value>
    /// The layout entity associated with the site.
    /// </value>
    public Layout Layout { get; set; } = default!;
}
