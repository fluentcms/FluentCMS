namespace FluentCMS.Api.Models;

/// <summary>
/// Represents a request to create a new site.
/// </summary>
public class SiteCreateRequest
{
    /// <summary>
    /// Gets or sets the name of the site.
    /// </summary>
    /// <value>
    /// The name of the site to be created.
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
    /// An array of URLs that will be associated with the site.
    /// </value>
    public string[] Urls { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the unique identifier of the default role for the site.
    /// </summary>
    /// <value>
    /// The unique identifier of the default role that will be associated with the site.
    /// </value>
    public Guid RoleId { get; set; }
}
