namespace FluentCMS.Repositories.EFCore;

public interface IEntityRepository<TEntity, TDBEntity> : IEntityRepository<TEntity> where TEntity : IEntity where TDBEntity : IEntityModel
{

}

public class EntityRepository<TEntity, TDBEntity>(FluentCmsDbContext dbContext, IMapper mapper) : IEntityRepository<TEntity, TDBEntity> where TEntity : class, IEntity where TDBEntity : class, IEntityModel
{
    protected readonly FluentCmsDbContext DbContext = dbContext;
    protected readonly IMapper Mapper = mapper;
    private readonly DbSet<TDBEntity> _dbSet = dbContext.Set<TDBEntity>();

    public virtual async Task<TEntity?> Create(TEntity entity, CancellationToken cancellationToken = default)
    {
        if (entity.Id == Guid.Empty)
            entity.Id = Guid.NewGuid();

        var dbEntity = Mapper.Map<TDBEntity>(entity);

        await _dbSet.AddAsync(dbEntity, cancellationToken);
        await DbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }

    public virtual async Task<IEnumerable<TEntity>> CreateMany(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        foreach (var entity in entities)
            if (entity.Id == Guid.Empty)
                entity.Id = Guid.NewGuid();

        var dbEntities = Mapper.Map<List<TDBEntity>>(entities);

        await _dbSet.AddRangeAsync(dbEntities, cancellationToken);
        await DbContext.SaveChangesAsync(cancellationToken);
        return entities;
    }

    public virtual async Task<TEntity?> Update(TEntity entity, CancellationToken cancellationToken = default)
    {
        var dbEntity = await _dbSet.FirstOrDefaultAsync(x => x.Id == entity.Id, cancellationToken);
        if (dbEntity == null)
            return null;

        Mapper.Map(entity, dbEntity); // Update dbEntity with the new data

        _dbSet.Update(dbEntity);

        await DbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public virtual async Task<TEntity?> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var dbEntity = await _dbSet.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (dbEntity == null)
            return null;

        _dbSet.Remove(dbEntity);
        await DbContext.SaveChangesAsync(cancellationToken);

        return Mapper.Map<TEntity>(dbEntity);
    }

    public virtual async Task<IEnumerable<TEntity>> DeleteMany(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var dbEntities = await _dbSet.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        if (dbEntities.Count == 0)
            return [];
        _dbSet.RemoveRange(dbEntities);
        await DbContext.SaveChangesAsync(cancellationToken);
        return Mapper.Map<IEnumerable<TEntity>>(dbEntities);
    }

    public virtual async Task<IEnumerable<TEntity>> GetAll(CancellationToken cancellationToken = default)
    {
        var dbEntities = await _dbSet.ToListAsync(cancellationToken);
        return Mapper.Map<IEnumerable<TEntity>>(dbEntities);
    }

    public virtual async Task<TEntity?> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var dbEntity = await _dbSet.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return Mapper.Map<TEntity>(dbEntity);
    }

    public virtual async Task<IEnumerable<TEntity>> GetByIds(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var dbEntities = await _dbSet.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        return Mapper.Map<IEnumerable<TEntity>>(dbEntities);
    }
}
