namespace FluentCMS.Repositories.EntityFramework;

public class Repository<TEntity, TContext>(TContext context, ILogger<Repository<TEntity, TContext>> logger) : IRepository<TEntity>
    where TEntity : class, IEntity
    where TContext : DbContext
{
    protected readonly ILogger<Repository<TEntity, TContext>> Logger = logger;
    protected readonly TContext Context = context;
    protected readonly DbSet<TEntity> DbSet = context.Set<TEntity>();

    #region Basic CRUD Operations

    public virtual async Task<TEntity> Add(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entity);

        if (entity.Id == Guid.Empty)
            entity.Id = Guid.NewGuid();

        try
        {
            await Context.AddAsync(entity, cancellationToken);
            await SaveChangesAsync(cancellationToken);

            Logger.LogInformation("Entity {EntityType} with id {EntityId} added", typeof(TEntity).Name, entity.Id);
            
            // Detach entity to prevent tracking issues in future operations
            Context.Entry(entity).State = EntityState.Detached;

            return entity;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to add entity {EntityType} with id {EntityId}", typeof(TEntity).Name, entity.Id);
            throw RepositoryException<TEntity>.ForEntityOperation("Add", entity.Id, $"Unable to add entity {typeof(TEntity).Name} with id {entity.Id}", ex);
        }
    }

    public virtual async Task<IEnumerable<TEntity>> AddRange(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entities);

        var entityList = entities.ToList();
        
        foreach (var entity in entityList)
        {
            if (entity.Id == Guid.Empty)
                entity.Id = Guid.NewGuid();
        }

        try
        {
            await Context.AddRangeAsync(entityList, cancellationToken);
            await SaveChangesAsync(cancellationToken);

            Logger.LogInformation("Added {Count} entities of type {EntityType}", entityList.Count, typeof(TEntity).Name);

            // Detach entities to prevent tracking issues in future operations
            foreach (var entity in entityList)
                Context.Entry(entity).State = EntityState.Detached;

            return entityList;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to add entities of type {EntityType}", typeof(TEntity).Name);
            throw RepositoryException<TEntity>.ForOperation("AddRange", $"Unable to add entities of type {typeof(TEntity).Name}", ex);
        }
    }

    public virtual async Task<TEntity> Update(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entity);

        try
        {
            DbSet.Update(entity);
            await SaveChangesAsync(cancellationToken);

            Logger.LogInformation("Entity {EntityType} with id {EntityId} updated", typeof(TEntity).Name, entity.Id);

            // Detach entity to prevent tracking issues in future operations
            Context.Entry(entity).State = EntityState.Detached;
            
            return entity;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to update entity {EntityType} with id {EntityId}", typeof(TEntity).Name, entity.Id);
            throw RepositoryException<TEntity>.ForEntityOperation("Update", entity.Id, $"Unable to update entity {typeof(TEntity).Name} with id {entity.Id}", ex);
        }
    }

    public virtual async Task<TEntity?> Remove(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entity);

        try
        {
            DbSet.Remove(entity);
            var affectedRows = await SaveChangesWithAffectedRowsAsync(cancellationToken);

            if (affectedRows == 0)
            {
                Logger.LogWarning("No entity was removed for {EntityType} with id {EntityId}", typeof(TEntity).Name, entity.Id);
                return null;
            }
            else if (affectedRows > 1)
            {
                Logger.LogWarning("More than one entity was removed for {EntityType} with id {EntityId}", typeof(TEntity).Name, entity.Id);
            }

            Logger.LogInformation("Entity {EntityType} with id {EntityId} removed", typeof(TEntity).Name, entity.Id);
            
            // Detach entity to prevent tracking issues in future operations
            Context.Entry(entity).State = EntityState.Detached;
            
            return entity;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to remove entity {EntityType} with id {EntityId}", typeof(TEntity).Name, entity.Id);
            throw RepositoryException<TEntity>.ForEntityOperation("Remove", entity.Id, $"Unable to remove entity {typeof(TEntity).Name} with id {entity.Id}", ex);
        }
    }

    public virtual async Task<TEntity?> Remove(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var entity = await DbSet.FindAsync([id], cancellationToken);
            if (entity == null)
            {
                Logger.LogWarning("Entity {EntityType} with id {EntityId} not found for removal", typeof(TEntity).Name, id);
                return null;
            }

            return await Remove(entity, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to remove entity {EntityType} with id {EntityId}", typeof(TEntity).Name, id);
            throw RepositoryException<TEntity>.ForEntityOperation("Remove", id, $"Unable to remove entity {typeof(TEntity).Name} with id {id}", ex);
        }
    }

    public virtual async Task<TEntity?> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await DbSet.AsNoTracking().SingleOrDefaultAsync(e => e.Id.Equals(id), cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to get entity {EntityType} with id {EntityId}", typeof(TEntity).Name, id);
            throw RepositoryException<TEntity>.ForEntityOperation("GetById", id, $"Unable to get entity {typeof(TEntity).Name} with id {id}", ex);
        }
    }

    public virtual async Task<IEnumerable<TEntity>> GetAll(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await DbSet.AsNoTracking().ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to get all entities of type {EntityType}", typeof(TEntity).Name);
            throw RepositoryException<TEntity>.ForOperation("GetAll", $"Unable to get all entities of type {typeof(TEntity).Name}", ex);
        }
    }

    #endregion

    #region Specification-based Query Methods

    public virtual async Task<IEnumerable<TEntity>> Query(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(specification);

        try
        {
            var query = specification.Apply(DbSet.AsNoTracking());
            return await query.ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to query entities of type {EntityType} using specification", typeof(TEntity).Name);
            throw RepositoryException<TEntity>.ForOperation("Query", $"Unable to query entities of type {typeof(TEntity).Name} using specification", ex);
        }
    }

    public virtual async Task<TEntity?> FirstOrDefault(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(specification);

        try
        {
            var query = specification.Apply(DbSet.AsNoTracking());
            return await query.FirstOrDefaultAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to find first entity of type {EntityType} using specification", typeof(TEntity).Name);
            throw RepositoryException<TEntity>.ForOperation("FirstOrDefault", $"Unable to find first entity of type {typeof(TEntity).Name} using specification", ex);
        }
    }

    public virtual async Task<TEntity?> SingleOrDefault(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(specification);

        try
        {
            var query = specification.Apply(DbSet.AsNoTracking());
            return await query.SingleOrDefaultAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to find single entity of type {EntityType} using specification", typeof(TEntity).Name);
            throw RepositoryException<TEntity>.ForOperation("SingleOrDefault", $"Unable to find single entity of type {typeof(TEntity).Name} using specification", ex);
        }
    }

    public virtual async Task<long> Count(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(specification);

        try
        {
            var query = specification.Apply(DbSet.AsNoTracking());
            return await query.LongCountAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to get long count of entities of type {EntityType} using specification", typeof(TEntity).Name);
            throw RepositoryException<TEntity>.ForOperation("LongCount", $"Unable to get long count of entities of type {typeof(TEntity).Name} using specification", ex);
        }
    }

    public virtual async Task<bool> Any(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(specification);

        try
        {
            var query = specification.Apply(DbSet.AsNoTracking());
            return await query.AnyAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to check if any entities of type {EntityType} exist using specification", typeof(TEntity).Name);
            throw RepositoryException<TEntity>.ForOperation("Any", $"Unable to check if any entities of type {typeof(TEntity).Name} exist using specification", ex);
        }
    }

    public async Task<PagedResult<TEntity>> FindPaged(ISpecification<TEntity> specification, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        // Get total count first
        var totalCount = await Count(specification, cancellationToken);

        // Get all items and apply pagination in memory (simplified approach)
        var allItems = await Query(specification, cancellationToken);
        var pagedItems = allItems.Skip((page - 1) * pageSize).Take(pageSize);

        return new PagedResult<TEntity>
        {
            Items = pagedItems,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    #endregion
        
    #region Aggregation

    public virtual async Task<decimal> Sum(ISpecification<TEntity> specification, Expression<Func<TEntity, decimal>> selector, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(selector);

        try
        {
            var query = specification.Apply(DbSet.AsNoTracking());
            return await query.SumAsync(selector, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to calculate sum for entities of type {EntityType} using specification", typeof(TEntity).Name);
            throw RepositoryException<TEntity>.ForOperation("Sum", $"Unable to calculate sum for entities of type {typeof(TEntity).Name} using specification", ex);
        }
    }

    #endregion

    #region Protected Helper Methods

    protected virtual async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await Context.SaveChangesAsync(cancellationToken);
        Logger.LogDebug("Changes saved for {EntityType}", typeof(TEntity).Name);
    }

    protected virtual async Task<int> SaveChangesWithAffectedRowsAsync(CancellationToken cancellationToken = default)
    {
        var result = await Context.SaveChangesAsync(cancellationToken);
        Logger.LogDebug("Changes saved for {EntityType} with {AffectedRows} affected rows", typeof(TEntity).Name, result);
        return result;
    }

   

    #endregion
}
