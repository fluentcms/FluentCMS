namespace FluentCMS.Entities;

/// <summary>
/// Represents the base contract for entities.
/// </summary>
public interface IEntity
{
    /// <summary>
    /// Gets or sets the unique identifier for the entity.
    /// </summary>
    Guid Id { get; set; }
}

/// <summary>
/// A basic implementation of <see cref="IEntity"/>.
/// </summary>
public abstract class Entity : IEntity
{
    /// <inheritdoc/>
    public Guid Id { get; set; }
}
