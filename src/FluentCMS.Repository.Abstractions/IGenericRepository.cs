using FluentCMS.Core.Entities;
using System.Linq.Expressions;

namespace FluentCMS.Repository.Abstractions
{
    public interface IGenericRepository<TKey, TEntity>
    where TKey : IEquatable<TKey>
    where TEntity : IEntity<TKey>
    {
        Task Create(TEntity entity, CancellationToken cancellationToken = default);
        Task Update(TEntity entity, CancellationToken cancellationToken = default);
        Task Delete(TKey id, CancellationToken cancellationToken = default);

        Task<IEnumerable<TEntity>> GetAll(CancellationToken cancellationToken = default);
        Task<IEnumerable<TEntity>> GetAll(Expression<Func<TEntity, bool>> filter, CancellationToken cancellationToken = default);
        Task<TEntity> GetById(TKey id, CancellationToken cancellationToken = default);
        Task<IEnumerable<TEntity>> GetByIds(IEnumerable<TKey> ids, CancellationToken cancellationToken = default);
    }

    public interface IGenericRepository<TEntity> : IGenericRepository<Guid, TEntity>
        where TEntity : IEntity
    {
    }
}
