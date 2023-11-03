using FluentCMS.Entities;

namespace FluentCMS.Application.Services;

public interface IGenericService<TEntity> where TEntity : class, IEntity
{
    Task<TEntity> Create(TEntity entity, CancellationToken cancellationToken = default);
    Task<TEntity> Delete(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<TEntity>> GetAll(CancellationToken cancellationToken = default);
    Task<TEntity> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<TEntity> Update(TEntity entity, CancellationToken cancellationToken = default);
}