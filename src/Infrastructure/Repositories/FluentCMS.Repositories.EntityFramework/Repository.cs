using FluentCMS.Repositories.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Repositories.EntityFramework;

public class Repository<TEntity, TDataContext>(TDataContext dataContext) : IRepository<TEntity>
    where TEntity : class
    where TDataContext : DbContext
{

    // Add single entity and persist changes
    public async Task<TEntity> Add(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entity);
        var entry = await dataContext.AddAsync(entity, cancellationToken);
        await dataContext.SaveChangesAsync(cancellationToken);
        return entry.Entity;
    }

    // Add range of entities and persist changes
    public async Task<IEnumerable<TEntity>> AddRange(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entities);
        await dataContext.AddRangeAsync(entities, cancellationToken);
        await dataContext.SaveChangesAsync(cancellationToken);
        return entities;
    }

    // Update entity and persist changes
    public async Task<TEntity> Update(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entity);
        var entry = dataContext.Update(entity);
        await dataContext.SaveChangesAsync(cancellationToken);
        return entry.Entity;
    }

    // Remove entity and persist changes
    public async Task<TEntity> Remove(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entity);
        var entry = dataContext.Remove(entity);
        await dataContext.SaveChangesAsync(cancellationToken);
        return entry.Entity;
    }

    // Single entry point for all queries - provides fluent API
    public IQuerySpecification<TEntity> Query()
    {
        return new QuerySpecification<TEntity>(dataContext.Set<TEntity>().AsNoTracking().AsQueryable());
    }
}
