namespace FluentCMS.Repositories.Abstractions;

public interface IRepository
{
}

public interface IRepository<TEntity> : IRepository where TEntity : class, IEntity
{
    // Existing CRUD operations
    Task<TEntity> Add(TEntity entity, CancellationToken cancellationToken = default);
    Task<IEnumerable<TEntity>> AddRange(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<TEntity> Update(TEntity entity, CancellationToken cancellationToken = default);
    Task<TEntity?> Remove(Guid id, CancellationToken cancellationToken = default);
    Task<TEntity?> Remove(TEntity entity, CancellationToken cancellationToken = default);
    Task<TEntity?> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<TEntity>> GetAll(CancellationToken cancellationToken = default);

    // Specification-based query methods
    Task<IEnumerable<TEntity>> Find(ISpecification<TEntity> specification, CancellationToken cancellationToken = default);
    Task<TEntity?> FindFirst(ISpecification<TEntity> specification, CancellationToken cancellationToken = default);
    Task<TEntity?> FindSingle(ISpecification<TEntity> specification, CancellationToken cancellationToken = default);
    Task<long> Count(ISpecification<TEntity> specification, CancellationToken cancellationToken = default);
    Task<bool> Any(ISpecification<TEntity> specification, CancellationToken cancellationToken = default);
    Task<PagedResult<TEntity>> FindPaged(ISpecification<TEntity> specification, int page, int pageSize, CancellationToken cancellationToken = default);

    // Basic aggregation method (keeping Sum as it's commonly used)
    Task<decimal> Sum(ISpecification<TEntity> specification, Expression<Func<TEntity, decimal>> selector, CancellationToken cancellationToken = default);
}
