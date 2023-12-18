namespace FluentCMS.Api.Models;

/// <summary>
/// Represents a request to update an existing site in the FluentCMS system.
/// </summary>
public class SiteUpdateRequest
{
    /// <summary>
    /// Gets or sets the unique identifier of the site to be updated.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the name of the site.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the description of the site.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the unique identifier of the role associated with the site.
    /// </summary>
    public Guid RoleId { get; set; }

    /// <summary>
    /// Gets or sets the list of URLs associated with the site.
    /// </summary>
    public List<string> Urls { get; set; } = [];
}
