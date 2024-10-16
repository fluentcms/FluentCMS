using System.Linq.Expressions;

namespace FluentCMS.Repositories.Postgres.Repositories.Base;

public abstract class EntityRepository<TEntity> : IEntityRepository<TEntity> where TEntity : Entity
{
    protected readonly DbSet<TEntity> Table;
    protected readonly PostgresDbContext Context;
    readonly IQueryable<TEntity> _queryable;

    public EntityRepository(PostgresDbContext context)
    {
        Table = context.Set<TEntity>();
        _queryable = Table.AsNoTrackingWithIdentityResolution().AsQueryable();
        Context = context;

    }

    protected async Task<TEntity?> GetByExpression(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return (await GetAllByExpression(predicate, cancellationToken)).SingleOrDefault();
    }

    protected async Task<IEnumerable<TEntity>> GetAllByExpression(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _queryable.Where(predicate).ToListAsync(cancellationToken);
    }


    public virtual async Task<IEnumerable<TEntity>> GetAll(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _queryable.ToListAsync(cancellationToken);
    }

    public virtual async Task<TEntity?> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _queryable.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public virtual async Task<IEnumerable<TEntity>> GetByIds(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var idsFilter = ids.ToList();
        return await Table.Where(x => idsFilter.Contains(x.Id)).ToListAsync(cancellationToken);
    }

    public virtual async Task<TEntity?> Create(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await Table.AddAsync(entity,  cancellationToken);
        await Context.SaveChangesAsync(cancellationToken);
        return result.Entity;
    }

    public virtual async Task<IEnumerable<TEntity>> CreateMany(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var enumerable = entities.ToList();
        await Table.AddRangeAsync(enumerable,  cancellationToken);
        await Context.SaveChangesAsync(cancellationToken);
        return enumerable;
    }

    public virtual async Task<TEntity?> Update(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = Table.Update(entity);
        await Context.SaveChangesAsync(cancellationToken);
        return result.Entity;

    }

    public virtual async Task<TEntity?> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entity = await GetById( id, cancellationToken);
        if (entity == null)
        {
            return null;
        }

        Table.Remove(entity);
        await Context.SaveChangesAsync(cancellationToken);


        return null;
    }

    public virtual async Task<IEnumerable<TEntity>> DeleteMany(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entities = await GetByIds(ids, cancellationToken);
        var entityIds = entities.Select(x => x.Id).ToList();
        await Table.Where(x => entityIds.Contains(x.Id)).ExecuteDeleteAsync(cancellationToken);
        return entities;
    }
}
