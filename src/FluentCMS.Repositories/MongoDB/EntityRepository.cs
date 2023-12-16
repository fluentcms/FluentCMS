using FluentCMS.Entities;
using Humanizer;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Globalization;

namespace FluentCMS.Repositories.MongoDB;

public abstract class EntityRepository<TEntity> : IEntityRepository<TEntity> where TEntity : IEntity
{
    protected readonly IMongoCollection<TEntity> Collection;
    protected readonly IMongoCollection<BsonDocument> BsonCollection;
    protected readonly IMongoDatabase MongoDatabase;
    protected readonly IMongoDBContext MongoDbContext;
    protected readonly IApplicationContext AppContext;

    public EntityRepository(IMongoDBContext mongoDbContext, IApplicationContext applicationContext)
    {
        MongoDatabase = mongoDbContext.Database;
        Collection = mongoDbContext.Database.GetCollection<TEntity>(GetCollectionName());
        BsonCollection = mongoDbContext.Database.GetCollection<BsonDocument>(GetCollectionName());
        MongoDbContext = mongoDbContext;
        AppContext = applicationContext;
    }

    protected virtual string GetCollectionName()
    {
        var entityType = typeof(TEntity).Name;

        var pluralizedName = entityType.Pluralize();

        return pluralizedName.ToLower(CultureInfo.InvariantCulture);
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

        await Collection.InsertOneAsync(entity, null, cancellationToken);

        return entity;
    }

    public virtual async Task<IEnumerable<TEntity>> CreateMany(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var entity in entities)
            entity.Id = Guid.NewGuid();

        await Collection.InsertManyAsync(entities, null, cancellationToken);

        return entities;
    }

    public virtual async Task<TEntity?> Update(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var idFilter = Builders<TEntity>.Filter.Eq(x => x.Id, entity.Id);

        await Collection.FindOneAndReplaceAsync(idFilter, entity, null, cancellationToken);

        return entity;
    }

    public virtual async Task<TEntity?> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var idFilter = Builders<TEntity>.Filter.Eq(doc => doc.Id, id);

        var entity = await Collection.FindOneAndDeleteAsync(idFilter, null, cancellationToken);

        return entity;
    }
}

public abstract class AuditEntityRepository<TEntity>(
    IMongoDBContext mongoDbContext,
    IApplicationContext applicationContext) :
    EntityRepository<TEntity>(mongoDbContext, applicationContext),
    IAuditEntityRepository<TEntity> where TEntity : IAuditEntity
{
    public override async Task<TEntity?> Create(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        entity.CreatedAt = DateTime.UtcNow;
        entity.CreatedBy = applicationContext.Current.Username;
        entity.LastUpdatedAt = DateTime.UtcNow;
        entity.LastUpdatedBy = applicationContext.Current.Username;

        return await base.Create(entity, cancellationToken);
    }

    public override async Task<TEntity?> Update(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        entity.LastUpdatedAt = DateTime.UtcNow;
        entity.LastUpdatedBy = applicationContext.Current.Username;

        return await base.Update(entity, cancellationToken);
    }

    public override Task<IEnumerable<TEntity>> CreateMany(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        foreach (var entity in entities)
        {
            entity.CreatedAt = DateTime.UtcNow;
            entity.CreatedBy = applicationContext.Current.Username;
            entity.LastUpdatedAt = DateTime.UtcNow;
            entity.LastUpdatedBy = applicationContext.Current.Username;
        }
        return base.CreateMany(entities, cancellationToken);
    }
}

public abstract class SiteAssociatedRepository<TEntity>(
    IMongoDBContext mongoDbContext,
    IApplicationContext applicationContext) :
    AuditEntityRepository<TEntity>(mongoDbContext, applicationContext),
    ISiteAssociatedRepository<TEntity> where TEntity : ISiteAssociatedEntity
{

    public async Task<IEnumerable<TEntity>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filter = Builders<TEntity>.Filter.Eq(x => x.SiteId, siteId);
        var findResult = await Collection.FindAsync(filter, null, cancellationToken);
        return await findResult.ToListAsync(cancellationToken);

    }

    public async Task<TEntity?> GetByIdForSite(Guid id, Guid siteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var idFilter = Builders<TEntity>.Filter.Eq(x => x.Id, id);
        var siteIdFilter = Builders<TEntity>.Filter.Eq(x => x.SiteId, siteId);

        var findResult = await Collection.FindAsync(Builders<TEntity>.Filter.And(idFilter, siteIdFilter), null, cancellationToken);

        return await findResult.SingleOrDefaultAsync(cancellationToken);
    }
}
