using FluentCMS.Repositories.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Repositories.EFCore;

public class EfEntitySet<TEntity>(DbSet<TEntity> dbSet) : IEntitySet<TEntity>
    where TEntity : class
{

    // Add entity to EF DbSet
    public async Task<TEntity> Add(TEntity entity, CancellationToken cancellationToken = default)
    {
        var entry = await dbSet.AddAsync(entity, cancellationToken);
        return entry.Entity;
    }

    // Add range of entities to EF DbSet
    public async Task<IEnumerable<TEntity>> AddRange(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        await dbSet.AddRangeAsync(entities, cancellationToken);
        return entities;
    }

    // Update entity in EF DbSet
    public Task<TEntity> Update(TEntity entity, CancellationToken cancellationToken = default)
    {
        var entry = dbSet.Update(entity);
        return Task.FromResult(entry.Entity);
    }

    // Remove entity from EF DbSet
    public Task<TEntity> Remove(TEntity entity, CancellationToken cancellationToken = default)
    {
        var entry = dbSet.Remove(entity);
        return Task.FromResult(entry.Entity);
    }
}
