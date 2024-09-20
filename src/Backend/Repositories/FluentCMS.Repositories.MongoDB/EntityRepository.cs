using Humanizer;

namespace FluentCMS.Repositories.MongoDB;

public abstract class EntityRepository<TEntity> : IEntityRepository<TEntity> where TEntity : IEntity
{
    protected readonly IMongoCollection<TEntity> Collection;
    protected readonly IMongoDatabase MongoDatabase;
    protected readonly IMongoDBContext MongoDbContext;

    public EntityRepository(IMongoDBContext mongoDbContext)
    {
        MongoDatabase = mongoDbContext.Database;
        Collection = mongoDbContext.Database.GetCollection<TEntity>(EntityRepository<TEntity>.GetCollectionName());
        MongoDbContext = mongoDbContext;

        // Ensure index on Id field
        var indexKeysDefinition = Builders<TEntity>.IndexKeys.Ascending(x => x.Id);
        Collection.Indexes.CreateOne(new CreateIndexModel<TEntity>(indexKeysDefinition));
    }

    private static string GetCollectionName()
    {
        var entityTypeName = typeof(TEntity).Name;
        return entityTypeName.Pluralize().ToLowerInvariant();
    }

    public virtual async Task<IEnumerable<TEntity>> GetAll(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filter = Builders<TEntity>.Filter.Empty;
        var findResult = await Collection.FindAsync(filter, null, cancellationToken);
        return await findResult.ToListAsync(cancellationToken);
    }

    public virtual async Task<TEntity?> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var idFilter = Builders<TEntity>.Filter.Eq(x => x.Id, id);
        var findResult = await Collection.FindAsync(idFilter, null, cancellationToken);
        return await findResult.SingleOrDefaultAsync(cancellationToken);
    }

    public virtual async Task<IEnumerable<TEntity>> GetByIds(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var idsFilter = Builders<TEntity>.Filter.In(x => x.Id, ids);
        var findResult = await Collection.FindAsync(idsFilter, null, cancellationToken);
        return await findResult.ToListAsync(cancellationToken);
    }

    public virtual async Task<TEntity?> Create(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = new InsertOneOptions { BypassDocumentValidation = false };
        await Collection.InsertOneAsync(entity, options, cancellationToken);
        return entity;
    }

    public virtual async Task<IEnumerable<TEntity>> CreateMany(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = new InsertManyOptions { BypassDocumentValidation = false };
        await Collection.InsertManyAsync(entities, options, cancellationToken);
        return entities;
    }

    public virtual async Task<TEntity?> Update(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var idFilter = Builders<TEntity>.Filter.Eq(x => x.Id, entity.Id);
        await Collection.ReplaceOneAsync(idFilter, entity, cancellationToken: cancellationToken);
        return entity;

    }

    public virtual async Task<TEntity?> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var idFilter = Builders<TEntity>.Filter.Eq(doc => doc.Id, id);
        var entity = await Collection.FindOneAndDeleteAsync(idFilter, null, cancellationToken);
        return entity;
    }

    public virtual async Task<IEnumerable<TEntity>> DeleteMany(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entities = await GetByIds(ids, cancellationToken);
        var entityIds = entities.Select(x => x.Id).ToList();
        var idsFilter = Builders<TEntity>.Filter.In(x => x.Id, entityIds);
        await Collection.DeleteManyAsync(idsFilter, cancellationToken);
        return entities;
    }
}
