namespace FluentCMS.Web.Api.Models;

/// <summary>
/// Represents a request to create a new role.
/// </summary>
public class RoleCreateRequest
{
    /// <summary>
    /// Gets or sets the name of the role.
    /// </summary>
    /// <value>
    /// The name of the role.
    /// </value>
    public string Name { get; set; } = default!;

    /// <summary>
    /// Gets or sets the description of the role.
    /// </summary>
    /// <value>
    /// The description of the role, or null if no description is provided.
    /// </value>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier of the site associated with the role.
    /// </summary>
    /// <value>
    /// The unique identifier of the site.
    /// </value>
    public Guid SiteId { get; set; }
}
