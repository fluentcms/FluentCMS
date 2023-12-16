namespace FluentCMS.Entities;

/// <summary>
/// Represents the base contract for audit entities, extending the basic entity contract.
/// </summary>
public interface IAuditEntity : IEntity
{
    /// <summary>
    /// Gets or sets the identifier of the user who created the entity.
    /// </summary>
    string CreatedBy { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the entity was created.
    /// </summary>
    DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the user who last updated the entity.
    /// </summary>
    string LastUpdatedBy { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the entity was last updated.
    /// </summary>
    DateTime LastUpdatedAt { get; set; }
}

/// <summary>
/// A basic implementation of <see cref="IAuditEntity"/>.
/// Inherits from <see cref="Entity"/>.
/// </summary>
public abstract class AuditEntity : IAuditEntity
{
    /// <inheritdoc/>
    public Guid Id { get; set; }

    /// <inheritdoc/>
    public string CreatedBy { get; set; } = string.Empty;

    /// <inheritdoc/>
    public DateTime CreatedAt { get; set; }

    /// <inheritdoc/>
    public string LastUpdatedBy { get; set; } = string.Empty;

    /// <inheritdoc/>
    public DateTime LastUpdatedAt { get; set; }
}
