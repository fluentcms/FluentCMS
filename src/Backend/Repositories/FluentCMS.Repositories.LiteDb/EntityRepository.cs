using Humanizer;
using LiteDB;

namespace FluentCMS.Repositories.LiteDb;

public class EntityRepository<TEntity> : IEntityRepository<TEntity> where TEntity : IEntity
{
    protected readonly ILiteCollectionAsync<TEntity> Collection;
    protected readonly ILiteCollectionAsync<BsonDocument> BsonCollection;
    protected readonly ILiteDatabaseAsync LiteDatabase;
    protected readonly ILiteDBContext LiteDbContext;

    public EntityRepository(ILiteDBContext liteDbContext)
    {
        LiteDatabase = liteDbContext.Database;
        Collection = liteDbContext.Database.GetCollection<TEntity>(EntityRepository<TEntity>.GetCollectionName());
        BsonCollection = liteDbContext.Database.GetCollection<BsonDocument>(EntityRepository<TEntity>.GetCollectionName());
        LiteDbContext = liteDbContext;
    }

    private static string GetCollectionName()
    {
        var entityTypeName = typeof(TEntity).Name;
        return entityTypeName.Pluralize().ToLowerInvariant();
    }

    public virtual async Task<IEnumerable<TEntity>> GetAll(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var findResult = await Collection.Query().ToListAsync();
        return [.. findResult];
    }

    public virtual async Task<TEntity?> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Query().Where(x => x.Id == id).SingleOrDefaultAsync();
    }

    public virtual async Task<IEnumerable<TEntity>> GetByIds(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var findResult = await Collection.Query().Where(x => ids.Contains(x.Id)).ToListAsync();
        return [.. findResult];
    }

    public virtual async Task<TEntity?> Create(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Collection.InsertAsync(entity);
        return entity;
    }

    public virtual async Task<IEnumerable<TEntity>> CreateMany(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entitiesList = entities.ToList();
        await Collection.InsertBulkAsync(entitiesList);
        return entitiesList;
    }

    public virtual async Task<TEntity?> Update(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Collection.UpdateAsync(entity.Id, entity);
        return entity;
    }

    public virtual async Task<TEntity?> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entity = await Collection.Query().Where(x => x.Id == id).SingleOrDefaultAsync();
        await Collection.DeleteAsync(id);
        return entity;
    }

    public virtual async Task<IEnumerable<TEntity>> DeleteMany(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entities = await GetByIds(ids, cancellationToken);
        var entityIds = entities.Select(x => x.Id).ToList();
        await Collection.DeleteManyAsync(x => entityIds.Contains(x.Id));
        return entities;
    }
}
