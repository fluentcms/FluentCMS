namespace FluentCMS.Repositories.EFCore;

public class EntityRepository<TEntity>(FluentCmsDbContext dbContext) : IEntityRepository<TEntity> where TEntity : class, IEntity
{
    protected readonly DbContext DbContext = dbContext;
    protected readonly DbSet<TEntity> DbSet = dbContext.Set<TEntity>();

    public virtual async Task<TEntity?> Create(TEntity entity, CancellationToken cancellationToken = default)
    {
        if (entity.Id == Guid.Empty)
            entity.Id = Guid.NewGuid();

        await DbSet.AddAsync(entity, cancellationToken);
        await DbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public virtual async Task<IEnumerable<TEntity>> CreateMany(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        foreach (var entity in entities)
            if (entity.Id == Guid.Empty)
                entity.Id = Guid.NewGuid();

        await DbSet.AddRangeAsync(entities, cancellationToken);
        await DbContext.SaveChangesAsync(cancellationToken);
        return entities;
    }

    public virtual async Task<TEntity?> Update(TEntity entity, CancellationToken cancellationToken = default)
    {
        DbSet.Update(entity);
        await DbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public virtual async Task<TEntity?> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await GetById(id, cancellationToken);
        if (entity != null)
        {
            DbSet.Remove(entity);
            await DbContext.SaveChangesAsync(cancellationToken);
        }
        return entity;
    }

    public virtual async Task<IEnumerable<TEntity>> DeleteMany(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var entities = await GetByIds(ids, cancellationToken);
        DbSet.RemoveRange(entities);
        await DbContext.SaveChangesAsync(cancellationToken);
        return entities;
    }

    public virtual async Task<IEnumerable<TEntity>> GetAll(CancellationToken cancellationToken = default)
    {
        return await DbSet.ToListAsync(cancellationToken);
    }

    public virtual async Task<TEntity?> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return await DbSet.FindAsync([id], cancellationToken);
    }

    public virtual async Task<IEnumerable<TEntity>> GetByIds(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        return await DbSet.Where(entity => ids.Contains(entity.Id)).ToListAsync(cancellationToken);
    }
}
