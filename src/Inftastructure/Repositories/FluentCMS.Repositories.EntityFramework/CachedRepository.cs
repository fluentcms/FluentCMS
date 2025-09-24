namespace FluentCMS.Repositories.EntityFramework;

public class CachedRepository<TEntity, TContext>(TContext context, IMemoryCache memoryCache, ILogger<CachedRepository<TEntity, TContext>> logger) : Repository<TEntity, TContext>(context, logger),
    ICachedRepository<TEntity>
    where TEntity : class, IEntity
    where TContext : DbContext
{
    private static string GetAllCacheKey => $"CachedRepository_{typeof(TEntity).Name}_GetAll";

    public override async Task<TEntity> Add(TEntity entity, CancellationToken cancellationToken = default)
    {
        var addedEntity = await base.Add(entity, cancellationToken);
        InvalidateCache();
        return addedEntity;
    }

    public override async Task<IEnumerable<TEntity>> AddRange(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        var result = await base.AddRange(entities, cancellationToken);
        InvalidateCache();
        return result;
    }

    public override async Task<TEntity?> Remove(TEntity entity, CancellationToken cancellationToken = default)
    {
        var removedEntity = await base.Remove(entity, cancellationToken);
        InvalidateCache();
        return removedEntity;
    }

    public override async Task<TEntity?> Remove(Guid id, CancellationToken cancellationToken = default)
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
        var entitiesDict = await GetCachedDictionary(cancellationToken);
        if (filter == null)
        {            
            return entitiesDict.Count != 0;
        }
        return entitiesDict.Values.AsQueryable().Any(filter);
    }

    public override async Task<long> Count(Expression<Func<TEntity, bool>>? filter = null, CancellationToken cancellationToken = default)
    {
        var entitiesDict = await GetCachedDictionary(cancellationToken);
        if (filter == null)
        {
            
            return entitiesDict.Count;
        }
        return entitiesDict.Values.AsQueryable().LongCount(filter);
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

        // Try to get from cache first
        if (memoryCache.TryGetValue(GetAllCacheKey, out Dictionary<Guid, TEntity>? cachedDict) && cachedDict != null)
        {
            return cachedDict;
        }

        // Use GetOrCreateAsync to handle concurrent access thread-safely
        return await memoryCache.GetOrCreateAsync(GetAllCacheKey, async factory =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var entities = await base.GetAll(cancellationToken);
            var entitiesDict = entities.ToDictionary(e => e.Id);

            factory.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            factory.SlidingExpiration = TimeSpan.FromMinutes(5);

            return entitiesDict;
        }) ?? [];
    }

    protected void InvalidateCache()
    {
        memoryCache.Remove(GetAllCacheKey);
    }
}
