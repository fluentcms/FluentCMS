using FluentCMS.Entities;
using LiteDB;
using LiteDB.Async;
using System.Linq.Expressions;

namespace FluentCMS.Repositories.LiteDb;

public class GenericRepository<TEntity> : IGenericRepository<TEntity>
    where TEntity : IEntity
{
    protected readonly ILiteCollectionAsync<TEntity> Collection;
    protected readonly ILiteCollectionAsync<BsonDocument> BsonCollection;
    protected readonly LiteDbContext DbContext;
    protected readonly IApplicationContext AppContext;

    public GenericRepository(LiteDbContext dbContext, IApplicationContext applicationContext)
    {
        DbContext = dbContext;
        Collection = dbContext.Database.GetCollection<TEntity>(GetCollectionName());
        BsonCollection = dbContext.Database.GetCollection<BsonDocument>(GetCollectionName());
        AppContext = applicationContext;
    }

    protected virtual string GetCollectionName()
    {
        return typeof(TEntity).Name;
    }

    public virtual async Task<TEntity?> Create(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        entity.Id = Guid.NewGuid();

        if (entity is IAuditEntity audit)
        {
            audit.CreatedAt = DateTime.UtcNow;
            audit.CreatedAt = DateTime.UtcNow;
            audit.LastUpdatedAt = DateTime.UtcNow;
            audit.LastUpdatedBy = AppContext.Current.UserName;
        }

        await Collection.InsertAsync(entity);

        return entity;
    }

    public virtual async Task<IEnumerable<TEntity>> CreateMany(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var entity in entities)
            entity.Id = Guid.NewGuid();

        if (typeof(TEntity) is IAuditEntity)
            foreach (var audit in entities.Cast<IAuditEntity>())
            {
                audit.CreatedAt = DateTime.UtcNow;
                audit.CreatedAt = DateTime.UtcNow;
                audit.LastUpdatedAt = DateTime.UtcNow;
                audit.LastUpdatedBy = AppContext.Current.UserName;
            }

        await Collection.InsertBulkAsync(entities);
        return entities;
    }

    public virtual async Task<TEntity?> Update(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (entity is IAuditEntity audit)
        {
            audit.LastUpdatedAt = DateTime.UtcNow;
            audit.LastUpdatedBy = AppContext.Current.UserName;
        }

        return await Collection.UpdateAsync(entity) ? entity : default;
    }

    public virtual async Task<TEntity?> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entity = await GetById(id, cancellationToken);
        if (entity is null)
            return default;

        var deleteCount = await Collection.DeleteManyAsync(x => x.Id == id);
        return deleteCount == 1 ? entity : default;
    }

    public virtual async Task<IEnumerable<TEntity>> GetAll(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.FindAllAsync();
    }

    protected virtual async Task<IEnumerable<TEntity>> GetAll(Expression<Func<TEntity, bool>> filter, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        //todo: Implement Pagination
        var result = await Collection.FindAsync(filter);
        return result.ToArray();
    }

    public virtual async Task<TEntity?> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = await Collection.FindByIdAsync(new BsonValue(id));
        return model;
    }

    public virtual Task<IEnumerable<TEntity>> GetByIds(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Collection.FindAsync(x => ids.Contains(x.Id));
    }
}
