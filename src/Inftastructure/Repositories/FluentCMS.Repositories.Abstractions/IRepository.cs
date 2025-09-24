namespace FluentCMS.Repositories.Abstractions;

public interface IRepository
{
}

public interface IRepository<TEntity> : IRepository where TEntity : class, IEntity
{
    Task<TEntity> Add(TEntity entity, CancellationToken cancellationToken = default);
    Task<IEnumerable<TEntity>> AddMany(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<TEntity> Update(TEntity entity, CancellationToken cancellationToken = default);
    Task<TEntity> Remove(TEntity entity, CancellationToken cancellationToken = default);
    Task<TEntity?> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<TEntity> Remove(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<TEntity>> GetAll(CancellationToken cancellationToken = default);
    Task<IEnumerable<TEntity>> Find(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
    Task<long> Count(Expression<Func<TEntity, bool>>? filter = default, CancellationToken cancellationToken = default);
    Task<bool> Any(Expression<Func<TEntity, bool>>? filter = default, CancellationToken cancellationToken = default);
}
