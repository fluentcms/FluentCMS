namespace FluentCMS.Entities;

/// <summary>
/// Represents an entity that is associated with a specific site.
/// This interface is used for implementing multi-tenancy by linking entities to specific sites.
/// Extends <see cref="IAuditEntity"/> to include audit fields.
/// </summary>
public interface ISiteAssociatedEntity : IAuditEntity
{
    /// <summary>
    /// Gets or sets the identifier of the site associated with the entity.
    /// </summary>
    Guid SiteId { get; set; }
}

/// <summary>
/// Abstract base class for entities associated with a specific site.
/// Inherits from <see cref="AuditEntity"/> to include audit fields.
/// </summary>
public abstract class SiteAssociatedEntity : AuditEntity, ISiteAssociatedEntity
{
    /// <inheritdoc />
    public Guid SiteId { get; set; }
}

