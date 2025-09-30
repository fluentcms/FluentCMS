using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.EntityFramework.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Repositories.EntityFramework;

public class EfRepository<TEntity, TContext>(TContext context, ILogger<EfRepository<TEntity, TContext>> logger) : IRepository<TEntity>
    where TEntity : class, IEntity
    where TContext : DbContext
{
    protected readonly ILogger<EfRepository<TEntity, TContext>> Logger = logger;
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

        // Validate that no entity in the collection is null
        for (int i = 0; i < entityList.Count; i++)
        {
            if (entityList[i] == null)
                throw new ArgumentNullException($"entities[{i}]", $"Entity at index {i} cannot be null");
        }

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
            var affectedRows = await SaveChangesAsync(cancellationToken);

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

    // Create a query specification for fluent querying
    public IQuerySpecification<TEntity> Query()
    {
        // Return a new query specification wrapping the DbSet as IQueryable
        return new EfQuerySpecification<TEntity>(DbSet.AsNoTracking().AsQueryable());
    }

    #endregion

    #region Protected Helper Methods

    protected virtual async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var result = await Context.SaveChangesAsync(cancellationToken);
        Logger.LogDebug("Changes saved for {EntityType}", typeof(TEntity).Name);
        return result;
    }

    #endregion
}

public class EfRepository<TEntity, TContext, TMarker>(TContext context, ILogger<EfRepository<TEntity, TContext>> logger) : EfRepository<TEntity, TContext>(context,logger), IRepository<TEntity, TMarker>
    where TEntity : class, IEntity
    where TContext : DbContext
    where TMarker : IDatabaseScopeMarker
{ }
