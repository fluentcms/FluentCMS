namespace FluentCMS.Infrastructure.Repositories.EntityFramework;

public class Repository<TEntity, TDataContext>(TDataContext dataContext) : IRepository<TEntity>
    where TEntity : class
    where TDataContext : DbContext
{
    protected readonly TDataContext DataContext = dataContext;
    protected readonly DbSet<TEntity> DbSet = dataContext.Set<TEntity>();

    // Add single entity and persist changes
    public virtual async Task<TEntity> Add(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entity);
        var entry = await DataContext.AddAsync(entity, cancellationToken);
        await DataContext.SaveChangesAsync(cancellationToken);
        return entry.Entity;
    }

    // Add range of entities and persist changes
    public virtual async Task<IEnumerable<TEntity>> AddRange(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entities);
        await DataContext.AddRangeAsync(entities, cancellationToken);
        await DataContext.SaveChangesAsync(cancellationToken);
        return entities;
    }

    // Update entity and persist changes
    public virtual async Task<TEntity> Update(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entity);
        var entry = DataContext.Update(entity);
        await DataContext.SaveChangesAsync(cancellationToken);
        return entry.Entity;
    }

    // Remove entity and persist changes
    public virtual async Task<TEntity> Remove(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entity);
        var entry = DataContext.Remove(entity);
        await DataContext.SaveChangesAsync(cancellationToken);
        return entry.Entity;
    }

    public virtual async Task<IEnumerable<TEntity>> RemoveRange(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entities);
        DataContext.RemoveRange(entities);
        await DataContext.SaveChangesAsync(cancellationToken);
        return entities;
    }

    // Single entry point for all queries - provides fluent API
    public virtual IQuerySpecification<TEntity> Query()
    {
        return new QuerySpecification<TEntity>(DbSet.AsNoTracking().AsQueryable());
    }
}
