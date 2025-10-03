using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Repositories;

public class BaseRepository<TEntity, TDataContext>(TDataContext dataContext) : IRepository<TEntity>
    where TEntity : class
    where TDataContext : IDataContext
{

    // Add single entity and persist changes
    public async Task<TEntity> Add(TEntity entity, CancellationToken cancellationToken = default)
    {
        var entitySet = dataContext.Set<TEntity>();
        var addedEntity = await entitySet.Add(entity, cancellationToken);
        await dataContext.SaveChanges(cancellationToken);
        return addedEntity;
    }

    // Add range of entities and persist changes
    public async Task<IEnumerable<TEntity>> AddRange(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        var entitySet = dataContext.Set<TEntity>();
        var addedEntities = await entitySet.AddRange(entities, cancellationToken);
        await dataContext.SaveChanges(cancellationToken);
        return addedEntities;
    }

    // Update entity and persist changes
    public async Task<TEntity> Update(TEntity entity, CancellationToken cancellationToken = default)
    {
        var entitySet = dataContext.Set<TEntity>();
        var updatedEntity = await entitySet.Update(entity, cancellationToken);
        await dataContext.SaveChanges(cancellationToken);
        return updatedEntity;
    }

    // Remove entity and persist changes
    public async Task<TEntity> Remove(TEntity entity, CancellationToken cancellationToken = default)
    {
        var entitySet = dataContext.Set<TEntity>();
        var removedEntity = await entitySet.Remove(entity, cancellationToken);
        await dataContext.SaveChanges(cancellationToken);
        return removedEntity;
    }

    // Single entry point for all queries - provides fluent API
    public IQuerySpecification<TEntity> Query()
    {
        return dataContext.CreateQuerySpecification<TEntity>();
    }
}
