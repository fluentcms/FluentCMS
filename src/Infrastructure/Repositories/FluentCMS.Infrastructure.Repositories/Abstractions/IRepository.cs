namespace FluentCMS.Infrastructure.Repositories.Abstractions;

public interface IRepository<TEntity>
    where TEntity : class
{
    // Core CRUD operations
    Task<TEntity> Add(TEntity entity, CancellationToken cancellationToken = default);
    Task<IEnumerable<TEntity>> AddRange(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<TEntity> Update(TEntity entity, CancellationToken cancellationToken = default);
    Task<TEntity> Remove(TEntity entity, CancellationToken cancellationToken = default);
    Task<IEnumerable<TEntity>> RemoveRange(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    // Single entry point for all queries - provides fluent API
    IQuerySpecification<TEntity> Query();
}
