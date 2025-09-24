namespace FluentCMS.Repositories.EntityFramework;

public class CachedRepository<TEntity, TContext>(TContext context, ICacheProvider cacheProvider, ILogger<CachedRepository<TEntity, TContext>> logger) : Repository<TEntity, TContext>(context, logger),
    ICachedRepository<TEntity>
    where TEntity : class, IEntity
    where TContext : DbContext
{
    private static string GetAllCacheKey => $"{typeof(TEntity).Name}_GetAll";

    public override async Task<TEntity> Add(TEntity entity, CancellationToken cancellationToken = default)
    {
        var addedEntity = await base.Add(entity, cancellationToken);
        InvalidateCache();
        return addedEntity;
    }

    public override async Task<IEnumerable<TEntity>> AddMany(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        var result = await base.AddMany(entities, cancellationToken);
        InvalidateCache();
        return result;
    }

    public override async Task<TEntity> Remove(TEntity entity, CancellationToken cancellationToken = default)
    {
        var removedEntity = await base.Remove(entity, cancellationToken);
        return removedEntity;
    }

    public override async Task<TEntity> Remove(Guid id, CancellationToken cancellationToken = default)
    {
        var removedEntity = await base.Remove(id, cancellationToken);
        InvalidateCache();
        return removedEntity;
    }

    public override async Task<TEntity> Update(TEntity entity, CancellationToken cancellationToken = default)
    {
        var updatedEntity = await base.Update(entity, cancellationToken);
        InvalidateCache();
        return updatedEntity;
    }

    public override async Task<TEntity?> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var entitiesDict = await GetCachedDictionary(cancellationToken);
        entitiesDict.TryGetValue(id, out var entity);
        return entity;
    }

    public override async Task<bool> Any(Expression<Func<TEntity, bool>>? filter = null, CancellationToken cancellationToken = default)
    {
        if (filter == null)
        {
            var entitiesDict = await GetCachedDictionary(cancellationToken);
            return entitiesDict.Count != 0;
        }
        return await base.Any(filter, cancellationToken);
    }

    public override async Task<long> Count(Expression<Func<TEntity, bool>>? filter = null, CancellationToken cancellationToken = default)
    {
        if (filter == null)
        {
            var entitiesDict = await GetCachedDictionary(cancellationToken);
            return entitiesDict.Count;
        }
        return await base.Count(filter, cancellationToken);
    }

    public override async Task<IEnumerable<TEntity>> Find(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        var entitiesDict = await GetCachedDictionary(cancellationToken);
        return [.. entitiesDict.Values.AsQueryable().Where(predicate)];
    }

    public override async Task<IEnumerable<TEntity>> GetAll(CancellationToken cancellationToken = default)
    {
        var entitiesDict = await GetCachedDictionary(cancellationToken);
        return entitiesDict.Values;
    }

    protected async Task<Dictionary<Guid, TEntity>> GetCachedDictionary(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // check if the cache contains the entities
        if (!await cacheProvider.TryGetValue(GetAllCacheKey, out Dictionary<Guid, TEntity>? entitiesDict, cancellationToken))
        {
            // if not, get the entities from the database
            var entities = await base.GetAll(cancellationToken);

            // create a dictionary with the entities
            // the key is entity id
            var entityDictionary = entities.ToDictionary(e => e.Id);

            // cache the entities
            await cacheProvider.Set(GetAllCacheKey, entityDictionary, absoluteExpiration: null, cancellationToken);

            // assign the newly created dictionary to entitiesDict
            entitiesDict = entityDictionary;
        }
        return entitiesDict ?? [];
    }

    protected void InvalidateCache()
    {
        cacheProvider.Remove(GetAllCacheKey);
    }
}
