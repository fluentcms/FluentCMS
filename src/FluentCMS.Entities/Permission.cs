namespace FluentCMS.Entities;

/// <summary>
/// Represents a permission associated with a site.
/// Inherits from <see cref="SiteAssociatedEntity"/> to establish a site-based context.
/// Defines permissions in terms of roles, entities, and specific policies.
/// </summary>
public class Permission : SiteAssociatedEntity
{
    /// <summary>
    /// Identifier of the associated role.
    /// </summary>
    public Guid RoleId { get; set; }

    /// <summary>
    /// Identifier of the entity to which this permission applies.
    /// </summary>
    public Guid EntityId { get; set; }

    /// <summary>
    /// Type of the entity.
    /// </summary>
    public string EntityType { get; set; } = default!;

    /// <summary>
    /// Specific policy associated with the permission.
    /// </summary>
    public string Policy { get; set; } = default!;
}
