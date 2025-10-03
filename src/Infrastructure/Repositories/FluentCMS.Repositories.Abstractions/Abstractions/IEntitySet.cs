namespace FluentCMS.Repositories.Abstractions;

public interface IEntitySet<TEntity>
    where TEntity : class
{
    // Add entity to the set
    Task<TEntity> Add(TEntity entity, CancellationToken cancellationToken = default);

    // Add range of entities to the set
    Task<IEnumerable<TEntity>> AddRange(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    // Update entity in the set
    Task<TEntity> Update(TEntity entity, CancellationToken cancellationToken = default);

    // Remove entity from the set
    Task<TEntity> Remove(TEntity entity, CancellationToken cancellationToken = default);
}
