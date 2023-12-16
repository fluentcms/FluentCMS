namespace FluentCMS.Entities;

/// <summary>
/// Represents a role within a specific site, extending the site-associated entity functionality.
/// </summary>
public class Role : SiteAssociatedEntity
{
    /// <summary>
    /// Gets or sets the name of the role. This field is required.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional description for the role.
    /// </summary>
    public string? Description { get; set; }
}
