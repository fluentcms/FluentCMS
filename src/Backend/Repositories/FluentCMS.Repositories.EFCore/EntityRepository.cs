namespace FluentCMS.Repositories.EFCore;

public class EntityRepository<TEntity>(FluentCmsDbContext dbContext) : IEntityRepository<TEntity> where TEntity : class, IEntity
{
    protected readonly FluentCmsDbContext DbContext = dbContext;
    private readonly DbSet<TEntity> _dbSet = dbContext.Set<TEntity>();

    public virtual async Task<TEntity?> Create(TEntity entity, CancellationToken cancellationToken = default)
    {
        if (entity.Id == Guid.Empty)
            entity.Id = Guid.NewGuid();

        await _dbSet.AddAsync(entity, cancellationToken);
        await DbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public virtual async Task<IEnumerable<TEntity>> CreateMany(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        foreach (var entity in entities)
            if (entity.Id == Guid.Empty)
                entity.Id = Guid.NewGuid();

        await _dbSet.AddRangeAsync(entities, cancellationToken);
        await DbContext.SaveChangesAsync(cancellationToken);
        return entities;
    }

    public virtual async Task<TEntity?> Update(TEntity entity, CancellationToken cancellationToken = default)
    {
        _dbSet.Update(entity);
        await DbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public virtual async Task<TEntity?> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await GetById(id, cancellationToken);
        if (entity != null)
        {
            _dbSet.Remove(entity);
            await DbContext.SaveChangesAsync(cancellationToken);
        }
        return entity;
    }

    public virtual async Task<IEnumerable<TEntity>> DeleteMany(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var entities = await GetByIds(ids, cancellationToken);
        _dbSet.RemoveRange(entities);
        await DbContext.SaveChangesAsync(cancellationToken);
        return entities;
    }

    public virtual async Task<IEnumerable<TEntity>> GetAll(CancellationToken cancellationToken = default)
    {
        return await _dbSet.ToListAsync(cancellationToken);
    }

    public virtual async Task<TEntity?> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet.FindAsync([id], cancellationToken);
    }

    public virtual async Task<IEnumerable<TEntity>> GetByIds(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        return await _dbSet.Where(entity => ids.Contains(entity.Id)).ToListAsync(cancellationToken);
    }
}
