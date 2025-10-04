using FluentCMS.Repositories.Abstractions;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

public class MongoEntitySet<TEntity>(IMongoCollection<TEntity> collection) : IEntitySet<TEntity>
    where TEntity : class
{

    // Insert single entity to MongoDB collection
    public async Task<TEntity> Add(TEntity entity, CancellationToken cancellationToken = default)
    {
        await collection.InsertOneAsync(entity, cancellationToken: cancellationToken);
        return entity;
    }

    // Insert multiple entities to MongoDB collection
    public async Task<IEnumerable<TEntity>> AddRange(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        await collection.InsertManyAsync(entities, cancellationToken: cancellationToken);
        return entities;
    }

    // Replace entity in MongoDB collection (assumes entity has Id field)
    public async Task<TEntity> Update(TEntity entity, CancellationToken cancellationToken = default)
    {
        // Assume TEntity has an Id property for filtering
        var idProperty = typeof(TEntity).GetProperty("Id");
        if (idProperty == null)
            throw new InvalidOperationException("Entity must have an Id property for update operations");

        var id = idProperty.GetValue(entity);
        var filter = Builders<TEntity>.Filter.Eq("Id", id);

        var result = await collection.ReplaceOneAsync(filter, entity, cancellationToken: cancellationToken);
        return entity;
    }

    // Delete entity from MongoDB collection (assumes entity has Id field)
    public async Task<TEntity> Remove(TEntity entity, CancellationToken cancellationToken = default)
    {
        // Assume TEntity has an Id property for filtering
        var idProperty = typeof(TEntity).GetProperty("Id") ??
            throw new InvalidOperationException("Entity must have an Id property for remove operations");

        var id = idProperty.GetValue(entity);
        var filter = Builders<TEntity>.Filter.Eq("Id", id);
        var result = await collection.DeleteOneAsync(filter, cancellationToken);
        if (result.DeletedCount != 1)
            throw new InvalidOperationException("Entity not found or could not be deleted");
        return entity;
    }
}
