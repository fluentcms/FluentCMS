namespace FluentCMS.Repositories.EntityFramework;

public class CachedRepository<TEntity, TContext>(TContext context, IMemoryCache memoryCache, ILogger<CachedRepository<TEntity, TContext>> logger) : Repository<TEntity, TContext>(context, logger),
    ICachedRepository<TEntity>
    where TEntity : class, IEntity
    where TContext : DbContext
{
    private static string GetAllCacheKey => $"CachedRepository_{typeof(TEntity).Name}_GetAll";

    #region Cache-Invalidating CRUD Operations

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

    #endregion

    #region Cached Read Operations

    public override async Task<TEntity?> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var entitiesDict = await GetCachedDictionary(cancellationToken);
        entitiesDict.TryGetValue(id, out var entity);
        return entity;
    }

    public override async Task<IEnumerable<TEntity>> GetAll(CancellationToken cancellationToken = default)
    {
        var entitiesDict = await GetCachedDictionary(cancellationToken);
        return entitiesDict.Values;
    }

    #endregion

    #region Cached Specification-based Operations

    public override async Task<IEnumerable<TEntity>> Find(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(specification);

        try
        {
            var entitiesDict = await GetCachedDictionary(cancellationToken);
            var query = specification.Apply(entitiesDict.Values.AsQueryable());
            return [.. query];
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to find entities of type {EntityType} using specification (cached)", typeof(TEntity).Name);
            throw RepositoryException<TEntity>.ForOperation("Find", $"Unable to find entities of type {typeof(TEntity).Name} using specification (cached)", ex);
        }
    }

    public override async Task<TEntity?> FindFirst(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(specification);

        try
        {
            var entitiesDict = await GetCachedDictionary(cancellationToken);
            var query = specification.Apply(entitiesDict.Values.AsQueryable());
            return query.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to find first entity of type {EntityType} using specification (cached)", typeof(TEntity).Name);
            throw RepositoryException<TEntity>.ForOperation("FindFirst", $"Unable to find first entity of type {typeof(TEntity).Name} using specification (cached)", ex);
        }
    }

    public override async Task<TEntity?> FindSingle(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(specification);

        try
        {
            var entitiesDict = await GetCachedDictionary(cancellationToken);
            var query = specification.Apply(entitiesDict.Values.AsQueryable());
            return query.SingleOrDefault();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to find single entity of type {EntityType} using specification (cached)", typeof(TEntity).Name);
            throw RepositoryException<TEntity>.ForOperation("FindSingle", $"Unable to find single entity of type {typeof(TEntity).Name} using specification (cached)", ex);
        }
    }

    public override async Task<int> Count(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(specification);

        try
        {
            var entitiesDict = await GetCachedDictionary(cancellationToken);
            var query = specification.Apply(entitiesDict.Values.AsQueryable());
            return query.Count();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to count entities of type {EntityType} using specification (cached)", typeof(TEntity).Name);
            throw RepositoryException<TEntity>.ForOperation("Count", $"Unable to count entities of type {typeof(TEntity).Name} using specification (cached)", ex);
        }
    }

    public override async Task<long> LongCount(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(specification);

        try
        {
            var entitiesDict = await GetCachedDictionary(cancellationToken);
            var query = specification.Apply(entitiesDict.Values.AsQueryable());
            return query.LongCount();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to get long count of entities of type {EntityType} using specification (cached)", typeof(TEntity).Name);
            throw RepositoryException<TEntity>.ForOperation("LongCount", $"Unable to get long count of entities of type {typeof(TEntity).Name} using specification (cached)", ex);
        }
    }

    public override async Task<bool> Any(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(specification);

        try
        {
            var entitiesDict = await GetCachedDictionary(cancellationToken);
            var query = specification.Apply(entitiesDict.Values.AsQueryable());
            return query.Any();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to check if any entities of type {EntityType} exist using specification (cached)", typeof(TEntity).Name);
            throw RepositoryException<TEntity>.ForOperation("Any", $"Unable to check if any entities of type {typeof(TEntity).Name} exist using specification (cached)", ex);
        }
    }

    public override async Task<bool> All(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(specification);

        try
        {
            var entitiesDict = await GetCachedDictionary(cancellationToken);
            var totalCount = entitiesDict.Count;
            var query = specification.Apply(entitiesDict.Values.AsQueryable());
            var matchingCount = query.Count();
            return totalCount == matchingCount;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to check if all entities of type {EntityType} satisfy specification (cached)", typeof(TEntity).Name);
            throw RepositoryException<TEntity>.ForOperation("All", $"Unable to check if all entities of type {typeof(TEntity).Name} satisfy specification (cached)", ex);
        }
    }

    public override async Task<decimal> Sum(ISpecification<TEntity> specification, Expression<Func<TEntity, decimal>> selector, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(selector);

        try
        {
            var entitiesDict = await GetCachedDictionary(cancellationToken);
            var query = specification.Apply(entitiesDict.Values.AsQueryable());
            return query.Sum(selector);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to calculate sum for entities of type {EntityType} using specification (cached)", typeof(TEntity).Name);
            throw RepositoryException<TEntity>.ForOperation("Sum", $"Unable to calculate sum for entities of type {typeof(TEntity).Name} using specification (cached)", ex);
        }
    }

    #endregion

    #region Projection Support (Falls back to base implementation)

    // Note: Projection operations typically involve complex queries that benefit less from simple caching
    // and may require database-specific optimizations, so we fall back to the base implementation
    public override async Task<IEnumerable<TResult>> FindProjected<TResult>(IProjectionSpecification<TResult> projection, CancellationToken cancellationToken = default)
    {
        // For complex projections, it's often better to go directly to the database
        // rather than loading all entities into memory and then projecting
        return await base.FindProjected(projection, cancellationToken);
    }

    public override async Task<TResult?> FindFirstProjected<TResult>(IProjectionSpecification<TResult> projection, CancellationToken cancellationToken = default) where TResult : default
    {
        // For complex projections, it's often better to go directly to the database
        return await base.FindFirstProjected(projection, cancellationToken);
    }

    #endregion

    #region Cache Management

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

            // Load entities from database using the base implementation
            var entities = await base.GetAll(cancellationToken);
            var entitiesDict = entities.ToDictionary(e => e.Id);

            // Set cache expiration policies
            factory.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            factory.SlidingExpiration = TimeSpan.FromMinutes(5);

            Logger.LogDebug("Cached {Count} entities of type {EntityType}", entitiesDict.Count, typeof(TEntity).Name);

            return entitiesDict;
        }) ?? [];
    }

    protected void InvalidateCache()
    {
        memoryCache.Remove(GetAllCacheKey);
        Logger.LogDebug("Cache invalidated for entity type {EntityType}", typeof(TEntity).Name);
    }

    #endregion
}
