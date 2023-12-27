using FluentCMS.Entities;
using Humanizer;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Globalization;

namespace FluentCMS.Repositories.MongoDB;

/// <summary>
/// Provides a base implementation for a repository that manages MongoDB entities.
/// </summary>
/// <typeparam name="TEntity">The type of the entity managed by this repository.</typeparam>
public abstract class EntityRepository<TEntity> : IEntityRepository<TEntity> where TEntity : IEntity
{
    protected readonly IMongoCollection<TEntity> Collection;
    protected readonly IMongoCollection<BsonDocument> BsonCollection;
    protected readonly IMongoDatabase MongoDatabase;
    protected readonly IMongoDBContext MongoDbContext;
    protected readonly IApplicationContext AppContext;

    /// <summary>
    /// Initializes a new instance of the EntityRepository class, setting up the necessary MongoDB collections.
    /// </summary>
    /// <param name="mongoDbContext">The MongoDB context used to access the database.</param>
    /// <param name="applicationContext">The application context for the repository.</param>
    public EntityRepository(IMongoDBContext mongoDbContext, IApplicationContext applicationContext)
    {
        MongoDatabase = mongoDbContext.Database;
        Collection = mongoDbContext.Database.GetCollection<TEntity>(GetCollectionName());
        BsonCollection = mongoDbContext.Database.GetCollection<BsonDocument>(GetCollectionName());
        MongoDbContext = mongoDbContext;
        AppContext = applicationContext;
    }

    /// <summary>
    /// Retrieves the collection name for the entity, pluralized and in lowercase.
    /// </summary>
    /// <returns>The collection name based on the entity type.</returns>
    protected virtual string GetCollectionName()
    {
        var entityType = typeof(TEntity).Name;
        var pluralizedName = entityType.Pluralize();
        return pluralizedName.ToLower(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Retrieves all entities in the collection.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation and returns a collection of entities.</returns>
    public virtual async Task<IEnumerable<TEntity>> GetAll(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filter = Builders<TEntity>.Filter.Empty;
        var findResult = await Collection.FindAsync(filter, null, cancellationToken);
        return await findResult.ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Retrieves an entity by its identifier.
    /// </summary>
    /// <param name="id">The identifier of the entity.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation and returns the entity if found.</returns>
    public virtual async Task<TEntity?> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var idFilter = Builders<TEntity>.Filter.Eq(x => x.Id, id);
        var findResult = await Collection.FindAsync(idFilter, null, cancellationToken);
        return await findResult.SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Retrieves multiple entities by their identifiers.
    /// </summary>
    /// <param name="ids">A collection of identifiers of the entities.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation and returns a collection of entities.</returns>
    public virtual async Task<IEnumerable<TEntity>> GetByIds(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var idsFilter = Builders<TEntity>.Filter.In(x => x.Id, ids);
        var findResult = await Collection.FindAsync(idsFilter, null, cancellationToken);
        return await findResult.ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a new entity in the collection.
    /// </summary>
    /// <param name="entity">The entity to create.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation and returns the created entity.</returns>
    public virtual async Task<TEntity?> Create(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        entity.Id = Guid.NewGuid();
        await Collection.InsertOneAsync(entity, null, cancellationToken);
        return entity;
    }

    /// <summary>
    /// Creates multiple new entities in the collection.
    /// </summary>
    /// <param name="entities">The collection of entities to create.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation and returns the collection of created entities.</returns>
    public virtual async Task<IEnumerable<TEntity>> CreateMany(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var entity in entities)
            entity.Id = Guid.NewGuid();
        await Collection.InsertManyAsync(entities, null, cancellationToken);
        return entities;
    }

    /// <summary>
    /// Updates an existing entity in the collection.
    /// </summary>
    /// <param name="entity">The entity to update.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation and returns the updated entity.</returns>
    public virtual async Task<TEntity?> Update(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var idFilter = Builders<TEntity>.Filter.Eq(x => x.Id, entity.Id);
        await Collection.FindOneAndReplaceAsync(idFilter, entity, null, cancellationToken);
        return entity;
    }

    /// <summary>
    /// Deletes an entity from the collection by its identifier.
    /// </summary>
    /// <param name="id">The identifier of the entity to delete.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation and returns the deleted entity.</returns>
    public virtual async Task<TEntity?> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var idFilter = Builders<TEntity>.Filter.Eq(doc => doc.Id, id);
        var entity = await Collection.FindOneAndDeleteAsync(idFilter, null, cancellationToken);
        return entity;
    }
}
