using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDb;

public class MongoDbGenericRepository<TEntity> : IGenericRepository<TEntity>
    where TEntity : IEntity
{
    protected readonly IMongoCollection<TEntity> Collection;
    protected readonly IMongoCollection<BsonDocument> BsonCollection;
    protected readonly IMongoDatabase MongoDatabase;
    protected readonly IMongoDBContext MongoDbContext;

    public MongoDbGenericRepository(IMongoDBContext mongoDbContext)
    {
        MongoDatabase = mongoDbContext.Database;
        Collection = mongoDbContext.Database.GetCollection<TEntity>(GetCollectionName());
        BsonCollection = mongoDbContext.Database.GetCollection<BsonDocument>(GetCollectionName());
        MongoDbContext = mongoDbContext;
    }

    protected virtual string GetCollectionName()
    {
        return typeof(TEntity).Name;
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

        entity.Id = Guid.NewGuid();

        if (entity is IAuditEntity audit)
        {
            audit.CreatedAt = DateTime.UtcNow;
            audit.LastUpdatedAt = DateTime.UtcNow;
        }

        await Collection.InsertOneAsync(entity, null, cancellationToken);

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
                audit.LastUpdatedAt = DateTime.UtcNow;
            }

        await Collection.InsertManyAsync(entities, null, cancellationToken);

        return entities;
    }

    public virtual async Task<TEntity?> Update(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (entity is IAuditEntity audit)
            audit.LastUpdatedAt = DateTime.UtcNow;

        var idFilter = Builders<TEntity>.Filter.Eq(x => x.Id, entity.Id);

        await Collection.FindOneAndReplaceAsync(idFilter, entity, null, cancellationToken);

        return entity;
    }

    public virtual async Task<TEntity?> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var idFilter = Builders<TEntity>.Filter.Eq(doc => doc.Id, id);

        var options = new FindOneAndDeleteOptions<TEntity>
        {
            //Projection = Builders<TEntity>.Projection.Include(doc => doc)
        };

        var entity = await Collection.FindOneAndDeleteAsync(idFilter, options, cancellationToken);
        return entity;
    }
}
