namespace FluentCMS.Repositories.EntityFramework;

public class Repository<TEntity, TContext>(TContext context, ILogger<Repository<TEntity, TContext>> logger) : IRepository<TEntity>, ITransactionalRepository<TEntity>
    where TEntity : class, IEntity
    where TContext : DbContext
{
    protected readonly ILogger<Repository<TEntity, TContext>> Logger = logger;
    protected readonly TContext Context = context;
    protected readonly DbSet<TEntity> DbSet = context.Set<TEntity>();
    private IDbContextTransaction? _currentTransaction;

    public bool IsTransactionActive => _currentTransaction != null;

    protected virtual async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Only save changes if not in a transaction
        if (!IsTransactionActive)
        {
            await Context.SaveChangesAsync(cancellationToken);
            Logger.LogInformation("Changes saved for {EntityType}", typeof(TEntity).Name);
        }
    }

    protected virtual async Task<int> SaveChangesWithAffectedRowsAsync(CancellationToken cancellationToken = default)
    {
        // Only save changes if not in a transaction
        if (!IsTransactionActive)
        {
            var result = await Context.SaveChangesAsync(cancellationToken);
            Logger.LogInformation("Changes saved for {EntityType} with {AffectedRows} affected rows", typeof(TEntity).Name, result);
            return result;
        }
        return 0; // No affected rows when in transaction
    }

    public virtual async Task BeginTransaction(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction != null)
        {
            Logger.LogError("A transaction is already active for {EntityType}", typeof(TEntity).Name);
            throw new RepositoryException<TEntity>("A transaction is already active.");
        }

        _currentTransaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        Logger.LogInformation("Transaction started for {EntityType}", typeof(TEntity).Name);
    }

    public virtual async Task Commit(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction == null)
        {
            Logger.LogError("No active transaction to commit for {EntityType}", typeof(TEntity).Name);
            throw new RepositoryException<TEntity>("No active transaction to commit.");
        }

        try
        {
            await _currentTransaction.CommitAsync(cancellationToken);
            Logger.LogInformation("Transaction committed for {EntityType}", typeof(TEntity).Name);
        }
        finally
        {
            await _currentTransaction.DisposeAsync();
            Logger.LogInformation("Transaction disposed for {EntityType}", typeof(TEntity).Name);
            _currentTransaction = null;
        }
    }

    public virtual async Task Rollback(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction == null)
        {
            Logger.LogError("No active transaction to rollback for {EntityType}", typeof(TEntity).Name);
            throw new RepositoryException<TEntity>("No active transaction to rollback.");
        }

        try
        {
            await _currentTransaction.RollbackAsync(cancellationToken);
            Logger.LogInformation("Transaction rolled back for {EntityType}", typeof(TEntity).Name);
        }
        finally
        {
            await _currentTransaction.DisposeAsync();
            Logger.LogInformation("Transaction disposed for {EntityType}", typeof(TEntity).Name);
            _currentTransaction = null;
        }
    }

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

    public virtual async Task<IEnumerable<TEntity>> AddMany(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var entity in entities)
        {
            if (entity.Id == Guid.Empty)
                entity.Id = Guid.NewGuid();
        }

        try
        {
            await Context.AddRangeAsync(entities, cancellationToken);
            await SaveChangesAsync(cancellationToken);

            Logger.LogInformation("Entities {EntityType} added", typeof(TEntity).Name);

            // Detach entities to prevent tracking issues in future operations
            foreach (var entity in entities)
                Context.Entry(entity).State = EntityState.Detached;

            return entities;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to AddMany entity {EntityType}", typeof(TEntity).Name);
            throw RepositoryException<TEntity>.ForOperation("AddMany", $"Unable to AddMany entity {typeof(TEntity).Name}", ex);
        }
    }

    public virtual async Task<TEntity> Remove(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entity);

        try
        {
            DbSet.Remove(entity);
            await SaveChangesAsync(cancellationToken);

            // Detach entity to prevent tracking issues in future operations
            Context.Entry(entity).State = EntityState.Detached;

            // Ensure exactly one row was affected
            var affectedRows = await SaveChangesWithAffectedRowsAsync(cancellationToken);
            if (affectedRows == 0)
            {
                Logger.LogError("Unable to Remove entity {EntityType} with id {EntityId}", typeof(TEntity).Name, entity.Id);
                throw RepositoryException<TEntity>.ForEntityOperation("Remove", entity.Id, $"Unable to Remove entity {typeof(TEntity).Name} with id {entity.Id}");
            }
            else if (affectedRows > 1)
            {
                Logger.LogError("More than one entity was removed for entity {EntityType} with id {EntityId}", typeof(TEntity).Name, entity.Id);
                throw RepositoryException<TEntity>.ForEntityOperation("Remove", entity.Id, $"More than one entity was removed for Entity {typeof(TEntity).Name} with id {entity.Id}");
            }

            Logger.LogInformation("Entity {EntityType} with id {EntityId} removed", typeof(TEntity).Name, entity.Id);
            return entity;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to Remove entity {EntityType} with id {EntityId}", typeof(TEntity).Name, entity.Id);
            throw RepositoryException<TEntity>.ForEntityOperation("Remove", entity.Id, $"Unable to Remove entity {typeof(TEntity).Name} with id {entity.Id}", ex);
        }
    }

    public virtual async Task<TEntity> Remove(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entity = await DbSet.FindAsync([id], cancellationToken) ??
            throw RepositoryException<TEntity>.ForEntityOperation("Remove", id, $"Entity {typeof(TEntity).Name} with id {id} not found.");

        return await Remove(entity, cancellationToken);
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

            Context.Entry(entity).State = EntityState.Detached;
            return entity;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to Update entity {EntityType} with id {EntityId}", typeof(TEntity).Name, entity.Id);
            throw RepositoryException<TEntity>.ForEntityOperation("Update", entity.Id, $"Unable to Update entity {typeof(TEntity).Name} with id {entity.Id}", ex);
        }
    }

    public virtual async Task<TEntity?> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await DbSet.SingleOrDefaultAsync(e => e.Id.Equals(id), cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable in GetById for entity {EntityType} with id {EntityId}", typeof(TEntity).Name, id);
            throw RepositoryException<TEntity>.ForEntityOperation("GetById", id, $"Unable in GetById for entity {typeof(TEntity).Name} with id {id}", ex);
        }
    }

    public virtual async Task<bool> Any(Expression<Func<TEntity, bool>>? filter = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (filter == null)
            {
                return await DbSet.AnyAsync(cancellationToken);
            }
            return await DbSet.AnyAsync(filter, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable in Any for entity {EntityType}", typeof(TEntity).Name);
            throw RepositoryException<TEntity>.ForOperation("Any", $"Unable in Any for entity {typeof(TEntity).Name}", ex);
        }
    }

    public virtual async Task<long> Count(Expression<Func<TEntity, bool>>? filter = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (filter == null)
            {
                return await DbSet.CountAsync(cancellationToken);
            }
            return await DbSet.CountAsync(filter, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable in Count for entity {EntityType}", typeof(TEntity).Name);
            throw RepositoryException<TEntity>.ForOperation("Count", $"Unable in Count for entity {typeof(TEntity).Name}", ex);
        }
    }

    public virtual async Task<IEnumerable<TEntity>> Find(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(predicate);

        try
        {
            return await DbSet.Where(predicate).ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable in Find for entity {EntityType}", typeof(TEntity).Name);
            throw RepositoryException<TEntity>.ForOperation("Find", $"Unable in Find for entity {typeof(TEntity).Name}", ex);
        }
    }

    public virtual async Task<IEnumerable<TEntity>> GetAll(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await DbSet.ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable in GetAll for entity {EntityType}", typeof(TEntity).Name);
            throw RepositoryException<TEntity>.ForOperation("GetAll", $"Unable in GetAll for entity {typeof(TEntity).Name}", ex);
        }
    }
}
