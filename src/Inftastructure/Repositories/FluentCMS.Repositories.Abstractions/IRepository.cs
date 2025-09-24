namespace FluentCMS.Repositories.Abstractions;

public interface IRepository
{
}

public interface IRepository<TEntity> : IRepository where TEntity : class, IEntity
{
    // Core CRUD operations
    Task<TEntity> Add(TEntity entity, CancellationToken cancellationToken = default);
    Task<IEnumerable<TEntity>> AddRange(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<TEntity> Update(TEntity entity, CancellationToken cancellationToken = default);
    Task<TEntity?> Remove(Guid id, CancellationToken cancellationToken = default);
    Task<TEntity?> Remove(TEntity entity, CancellationToken cancellationToken = default);
    Task<TEntity?> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<TEntity>> GetAll(CancellationToken cancellationToken = default);

    // Core specification-based query methods
    Task<IEnumerable<TEntity>> Query(ISpecification<TEntity> specification, CancellationToken cancellationToken = default);
    Task<TEntity?> FirstOrDefault(ISpecification<TEntity> specification, CancellationToken cancellationToken = default);
    Task<TEntity?> SingleOrDefault(ISpecification<TEntity> specification, CancellationToken cancellationToken = default);
    Task<long> Count(ISpecification<TEntity> specification, CancellationToken cancellationToken = default);
    Task<bool> Any(ISpecification<TEntity> specification, CancellationToken cancellationToken = default);
    Task<PagedResult<TEntity>> FindPaged(ISpecification<TEntity> specification, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<decimal> Sum(ISpecification<TEntity> specification, Expression<Func<TEntity, decimal>> selector, CancellationToken cancellationToken = default);
}
