using Microsoft.Extensions.Caching.Memory;

namespace FluentCMS.Repositories.Caching;

public abstract class EntityRepository<TEntity>(IEntityRepository<TEntity> entityRepository, IMemoryCache memoryCache) : IEntityRepository<TEntity> where TEntity : IEntity
{
    private static string GetAllCacheKey => $"{typeof(TEntity).Name}_GetAll";

    private async Task<Dictionary<Guid, TEntity>> GetCachedDictionary(CancellationToken cancellationToken = default)
    {
        // check if the cache contains the entities
        if (!memoryCache.TryGetValue(GetAllCacheKey, out Dictionary<Guid, TEntity>? entitiesDict))
        {
            // if not, get the entities from the database
            var entities = await entityRepository.GetAll(cancellationToken);

            // create a dictionary with the entities
            // the key is entity id
            var entityDictionary = entities.ToDictionary(e => e.Id);

            // cache the entities
            memoryCache.Set(GetAllCacheKey, entityDictionary);

            // assign the newly created dictionary to entitiesDict
            entitiesDict = entityDictionary;
        }
        return entitiesDict ?? [];
    }

    private async Task Set(TEntity entity, CancellationToken cancellationToken = default)
    {
        // access to cached dictionary
        var entitiesDict = await GetCachedDictionary(cancellationToken);

        // set the entity in the dictionary
        entitiesDict[entity.Id] = entity;
    }

    public async Task<TEntity?> Create(TEntity entity, CancellationToken cancellationToken = default)
    {
        var newEntity = await entityRepository.Create(entity, cancellationToken);
        if (newEntity != null)
        {
            await Set(newEntity, cancellationToken);
        }
        return newEntity;
    }

    public async Task<IEnumerable<TEntity>> CreateMany(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        var newEntities = await entityRepository.CreateMany(entities, cancellationToken);
        if (newEntities != null)
        {
            foreach (var entity in newEntities)
            {
                await Set(entity, cancellationToken);
            }
        }
        return newEntities ?? [];
    }

    public async Task<TEntity?> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        // access to cached dictionary
        var entitiesDict = await GetCachedDictionary(cancellationToken);

        // remove the entity from the dictionary
        if (entitiesDict.Remove(id, out var entity))
        {
            // remove the entity from the database
            await entityRepository.Delete(id, cancellationToken);
            return entity;
        }
        return default;
    }

    public async Task<IEnumerable<TEntity>> DeleteMany(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        // access to cached dictionary
        var entitiesDict = await GetCachedDictionary(cancellationToken);

        // remove the entities from the dictionary
        var entities = new List<TEntity>();
        foreach (var id in ids)
        {
            if (entitiesDict.Remove(id, out var entity))
            {
                entities.Add(entity);
            }
        }

        // remove the entities from the database
        await entityRepository.DeleteMany(ids, cancellationToken);

        return entities;
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
        // access to cached dictionary
        var entitiesDict = await GetCachedDictionary(cancellationToken);

        // update the entity in the database
        var updatedEntity = await entityRepository.Update(entity, cancellationToken);

        // update the entity in the dictionary
        if (updatedEntity != null && entitiesDict.ContainsKey(entity.Id))
        {
            entitiesDict[entity.Id] = updatedEntity;
        }
        return updatedEntity;
    }
}
