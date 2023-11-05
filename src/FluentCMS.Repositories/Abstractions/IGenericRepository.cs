using FluentCMS.Entities;

namespace FluentCMS.Repositories.Abstractions;

public interface IGenericRepository<TEntity>
    where TEntity : IEntity
{
    Task<TEntity?> Create(TEntity entity, CancellationToken cancellationToken = default);
    Task<IEnumerable<TEntity>> CreateMany(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<TEntity?> Update(TEntity entity, CancellationToken cancellationToken = default);
    Task<TEntity?> Delete(Guid id, CancellationToken cancellationToken = default);

    Task<IEnumerable<TEntity>> GetAll(CancellationToken cancellationToken = default);
    Task<TEntity?> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<TEntity>> GetByIds(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
}
