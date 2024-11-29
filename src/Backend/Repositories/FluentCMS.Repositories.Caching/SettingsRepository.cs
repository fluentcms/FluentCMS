namespace FluentCMS.Repositories.Caching;

public class SettingsRepository(ISettingsRepository settingsRepository, ICacheProvider cacheProvider) : ISettingsRepository
{
    private static string GetAllCacheKey => $"{typeof(Settings).Name}_GetAll";

    public async Task<IEnumerable<Settings>> GetAll(CancellationToken cancellationToken = default)
    {
        var entitiesDict = await GetCachedDictionary(cancellationToken);
        return entitiesDict.Values;
    }

    public async Task<Settings?> GetById(Guid entityId, CancellationToken cancellationToken = default)
    {
        // access to cached dictionary
        var entitiesDict = await GetCachedDictionary(cancellationToken);

        // get the entity from the dictionary
        if (entitiesDict.TryGetValue(entityId, out var entity))
        {
            return entity;
        }
        return default;
    }

    public async Task<IEnumerable<Settings>> GetByIds(IEnumerable<Guid> entityIds, CancellationToken cancellationToken = default)
    {
        // access to cached dictionary
        var entitiesDict = await GetCachedDictionary(cancellationToken);

        // get the entities from the dictionary
        var entities = new List<Settings>();
        foreach (var id in entityIds)
        {
            if (entitiesDict.TryGetValue(id, out var entity))
            {
                entities.Add(entity);
            }
        }
        return entities;
    }

    public async Task<Settings?> Update(Guid entityId, Dictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        var updatedEntity = await settingsRepository.Update(entityId, settings, cancellationToken);
        InvalidateCache();
        return updatedEntity;
    }

    protected async Task<Dictionary<Guid, Settings>> GetCachedDictionary(CancellationToken cancellationToken = default)
    {
        // check if the cache contains the entities
        if (!cacheProvider.TryGetValue(GetAllCacheKey, out Dictionary<Guid, Settings>? entitiesDict))
        {
            // if not, get the entities from the database
            var entities = await settingsRepository.GetAll(cancellationToken);

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

    protected void InvalidateCache()
    {
        cacheProvider.Remove(GetAllCacheKey);
    }
}
