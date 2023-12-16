using FluentCMS.Entities;

namespace FluentCMS.Repositories;

/// <summary>
/// Defines a generic repository for CRUD operations on entities.
/// </summary>
/// <typeparam name="TEntity">The type of entity this repository works with, constrained to IEntity.</typeparam>
public interface IEntityRepository<TEntity> where TEntity : IEntity
{
    /// <summary>
    /// Asynchronously creates a new entity in the database.
    /// </summary>
    /// <param name="entity">The entity to create.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The created entity, or null if the operation fails.</returns>
    Task<TEntity?> Create(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously creates multiple entities in the database.
    /// </summary>
    /// <param name="entities">The collection of entities to create.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An enumeration of the created entities.</returns>
    Task<IEnumerable<TEntity>> CreateMany(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously updates an existing entity in the database.
    /// </summary>
    /// <param name="entity">The entity to update.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The updated entity, or null if the update fails.</returns>
    Task<TEntity?> Update(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously deletes an entity from the database by its ID.
    /// </summary>
    /// <param name="id">The unique identifier of the entity to delete.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The deleted entity, or null if no entity was found with the given ID.</returns>
    Task<TEntity?> Delete(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously retrieves all entities of the specified type from the database.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An enumeration of all entities of the specified type.</returns>
    Task<IEnumerable<TEntity>> GetAll(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously retrieves an entity by its ID.
    /// </summary>
    /// <param name="id">The unique identifier of the entity to retrieve.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The entity with the specified ID, or null if no such entity exists.</returns>
    Task<TEntity?> GetById(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously retrieves multiple entities by their IDs.
    /// </summary>
    /// <param name="ids">The collection of unique identifiers of the entities to retrieve.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An enumeration of entities with the specified IDs.</returns>
    Task<IEnumerable<TEntity>> GetByIds(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
}
