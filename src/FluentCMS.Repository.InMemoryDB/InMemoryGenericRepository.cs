using System.Linq.Expressions;
using FluentCMS.Repository.Abstractions;
using FluentCMS.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Repository.InMemoryDB;

public class InMemoryGenericRepository<TEntity> : InMemoryGenericRepository<Guid, TEntity>, IGenericRepository<TEntity>
    where TEntity : class, IEntity
{
    public InMemoryGenericRepository(InMemoryDBContext<Guid, TEntity> dBContext) : base(dBContext)
    {
    }
}

public class InMemoryGenericRepository<TKey, TEntity> : IGenericRepository<TKey, TEntity>
        where TKey : IEquatable<TKey>
        where TEntity : class, IEntity<TKey>
{

    private readonly InMemoryDBContext<TKey, TEntity> _dBContext;

    public InMemoryGenericRepository(InMemoryDBContext<TKey, TEntity> dBContext)
    {
        _dBContext = dBContext;
    }

    public async Task Create(TEntity entity, CancellationToken cancellationToken = default)
    {
        await _dBContext.DbSet.AddAsync(entity, cancellationToken);
        await _dBContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Delete(TKey id, CancellationToken cancellationToken = default)
    {
        var item = await GetById(id, cancellationToken);
        if (item != null)
        {
            _dBContext.DbSet.Remove(item);
            await _dBContext.SaveChangesAsync(cancellationToken);
        }
    }
    public async Task Update(TEntity entity, CancellationToken cancellationToken = default)
    {
        _dBContext.DbSet.Update(entity);
        await _dBContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IEnumerable<TEntity>> GetAll(CancellationToken cancellationToken = default)
    {
        return await _dBContext.DbSet.ToArrayAsync(cancellationToken);
    }

    public async Task<IEnumerable<TEntity>> GetAll(Expression<Func<TEntity, bool>> filter, CancellationToken cancellationToken = default)
    {
        return await _dBContext.DbSet.Where(filter).ToArrayAsync(cancellationToken);
    }

    public async Task<TEntity> GetById(TKey id, CancellationToken cancellationToken = default)
    {
        return await _dBContext.DbSet.Where(x => x.Id.Equals(id)).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IEnumerable<TEntity>> GetByIds(IEnumerable<TKey> ids, CancellationToken cancellationToken = default)
    {
        return await _dBContext.DbSet.Where(x => ids.Contains(x.Id)).ToArrayAsync(cancellationToken);
    }

}
