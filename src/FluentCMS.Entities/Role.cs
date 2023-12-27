namespace FluentCMS.Entities;

/// <summary>
/// Represents a role within a specific site.
/// Inherits from <see cref="SiteAssociatedEntity"/> to establish a site-based context.
/// Defines roles for user access and permissions management.
/// </summary>
public class Role : SiteAssociatedEntity
{
    /// <summary>
    /// Name of the role. This field is required.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Optional description for the role. Provides additional context about the role's purpose or use.
    /// </summary>
    public string? Description { get; set; }
}
