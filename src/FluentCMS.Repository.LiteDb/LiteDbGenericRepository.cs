using FluentCMS.Entities;
using LiteDB;
using LiteDB.Async;
using System.Linq.Expressions;

namespace FluentCMS.Repository.LiteDb;

// TODO: this class could be internal. its public for now because the tests
public class LiteDbGenericRepository<TKey, TEntity> : IGenericRepository<TKey, TEntity>
    where TKey : IEquatable<TKey>
    where TEntity : IEntity<TKey>
{
    protected ILiteCollectionAsync<TEntity> Collection { get; }
    protected ILiteCollectionAsync<BsonDocument> BsonCollection { get; }
    protected LiteDbContext DbContext { get; private set; }

    public LiteDbGenericRepository(LiteDbContext dbContext)
    {
        DbContext = dbContext;
        Collection = dbContext.Database.GetCollection<TEntity>(GetCollectionName());
        BsonCollection = dbContext.Database.GetCollection<BsonDocument>(GetCollectionName());
    }

    protected virtual string GetCollectionName()
    {
        return typeof(TEntity).Name;
    }

    public virtual Task Create(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ////todo: implement ApplicationContext
        ////If the entity is extend from IAuditEntity, the audit properties (CreatedAt, CreatedBy, etc.) should be set
        if (entity is IAuditEntity<TKey> audit)
            SetPropertiesOnCreate(audit);

        return Collection.InsertAsync(entity);
    }

    public virtual Task CreateMany(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ////todo: implement ApplicationContext
        ////If the entity is extend from IAuditEntity, the audit properties (CreatedAt, CreatedBy, etc.) should be set
        if (typeof(TEntity) is IAuditEntity<TKey>)
        {
            foreach (var audit in entities.Cast<IAuditEntity<TKey>>())
                SetPropertiesOnCreate(audit);
        }

        return Collection.InsertBulkAsync(entities);
    }

    private void SetPropertiesOnCreate(IAuditEntity<TKey> audit)
    {
        audit.CreatedAt = DateTime.UtcNow;
        //todo set user
    }

    public virtual Task Delete(TKey id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Collection.DeleteAsync(new BsonValue(id));
    }

    public virtual async Task<IEnumerable<TEntity>> GetAll(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await Collection.FindAllAsync();
        return result.ToArray();
    }

    public virtual async Task<IEnumerable<TEntity>> GetAll(Expression<Func<TEntity, bool>> filter, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        //todo: Implement Pagination

        var result = await Collection.FindAsync(filter);
        return result.ToArray();
    }

    public virtual async Task<TEntity?> GetById(TKey id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = await Collection.FindByIdAsync(new BsonValue(id));
        return model;
    }

    public virtual Task<IEnumerable<TEntity>> GetByIds(IEnumerable<TKey> ids, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Collection.FindAsync(x => ids.Contains(x.Id));
    }

    public virtual Task Update(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ////todo: implement ApplicationContext
        ////If the entity is extend from IAuditEntity, the audit properties (LastUpdatedAt, LastUpdatedBy, etc.) should be set
        if (entity is IAuditEntity<TKey> audit)
            SetPropertiesOnUpdate(audit);

        return Collection.UpdateAsync(entity);
    }

    private void SetPropertiesOnUpdate(IAuditEntity<TKey> audit)
    {
        audit.LastUpdatedAt = DateTime.UtcNow;
        //todo set user
    }
}

public class LiteDbGenericRepository<TEntity> : LiteDbGenericRepository<Guid, TEntity>, IGenericRepository<TEntity>
    where TEntity : IEntity
{
    public LiteDbGenericRepository(LiteDbContext dbContext) : base(dbContext)
    {
    }
}
