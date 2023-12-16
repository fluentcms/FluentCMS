using FluentCMS.Entities;

namespace FluentCMS.Repositories;

/// <summary>
/// Defines a repository for entities associated with a specific site.
/// </summary>
/// <typeparam name="TEntity">The type of entity this repository works with, constrained to ISiteAssociatedEntity.</typeparam>
public interface ISiteAssociatedRepository<TEntity> : IAuditEntityRepository<TEntity> where TEntity : ISiteAssociatedEntity
{
    /// <summary>
    /// Asynchronously retrieves all entities of the specified type that are associated with a given site.
    /// </summary>
    /// <param name="siteId">The unique identifier of the site.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An enumeration of entities associated with the specified site.</returns>
    Task<IEnumerable<TEntity>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously retrieves an entity by its ID, ensuring it belongs to a specific site.
    /// </summary>
    /// <param name="id">The unique identifier of the entity to retrieve.</param>
    /// <param name="siteId">The unique identifier of the site.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The entity with the specified ID, or null if no such entity exists within the specified site.</returns>
    Task<TEntity?> GetByIdForSite(Guid id, Guid siteId, CancellationToken cancellationToken = default);
}

