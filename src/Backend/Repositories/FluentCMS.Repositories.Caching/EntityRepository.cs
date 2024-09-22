namespace FluentCMS.Repositories.Caching;

public abstract class EntityRepository<TEntity>(IEntityRepository<TEntity> entityRepository, ICacheProvider cacheProvider) : IEntityRepository<TEntity> where TEntity : IEntity
{
    private static string GetAllCacheKey => $"{typeof(TEntity).Name}_GetAll";

    public async Task<TEntity?> Create(TEntity entity, CancellationToken cancellationToken = default)
    {
        var newEntity = await entityRepository.Create(entity, cancellationToken);
        InvalidateCache();
        return newEntity;
    }

    public async Task<IEnumerable<TEntity>> CreateMany(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        var newEntities = await entityRepository.CreateMany(entities, cancellationToken);
        InvalidateCache();
        return newEntities ?? [];
    }

    public async Task<TEntity?> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var deleted = await entityRepository.Delete(id, cancellationToken);
        InvalidateCache();
        return deleted;
    }

    public async Task<IEnumerable<TEntity>> DeleteMany(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var deleted = await entityRepository.DeleteMany(ids, cancellationToken);
        InvalidateCache();
        return deleted;
    }

    public async Task<IEnumerable<TEntity>> GetAll(CancellationToken cancellationToken = default)
    {
        var entitiesDict = await GetCachedDictionary(cancellationToken);
        return entitiesDict.Values;
    }

    public async Task<TEntity?> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        // access to cached dictionary
        var entitiesDict = await GetCachedDictionary(cancellationToken);

        // get the entity from the dictionary
        if (entitiesDict.TryGetValue(id, out var entity))
        {
            return entity;
        }
        return default;
    }

    public async Task<IEnumerable<TEntity>> GetByIds(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        // access to cached dictionary
        var entitiesDict = await GetCachedDictionary(cancellationToken);

        // get the entities from the dictionary
        var entities = new List<TEntity>();
        foreach (var id in ids)
        {
            if (entitiesDict.TryGetValue(id, out var entity))
            {
                entities.Add(entity);
            }
        }
        return entities;
    }

    public async Task<TEntity?> Update(TEntity entity, CancellationToken cancellationToken = default)
    {
        var updatedEntity = await entityRepository.Update(entity, cancellationToken);
        InvalidateCache();
        return updatedEntity;
    }

    protected void InvalidateCache()
    {
        cacheProvider.Remove(GetAllCacheKey);
    }

    protected async Task<Dictionary<Guid, TEntity>> GetCachedDictionary(CancellationToken cancellationToken = default)
    {
        // check if the cache contains the entities
        if (!cacheProvider.TryGetValue(GetAllCacheKey, out Dictionary<Guid, TEntity>? entitiesDict))
        {
            // if not, get the entities from the database
            var entities = await entityRepository.GetAll(cancellationToken);

            // create a dictionary with the entities
            // the key is entity id
            var entityDictionary = entities.ToDictionary(e => e.Id);

            // cache the entities
            cacheProvider.Set(GetAllCacheKey, entityDictionary);

            // assign the newly created dictionary to entitiesDict
            entitiesDict = entityDictionary;
        }
        return entitiesDict ?? [];
    }
}
