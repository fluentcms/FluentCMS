using FluentCMS.Repositories.Abstractions;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

public class MongoDataContext(IMongoDatabase database) : IDataContext
{
    // SaveChanges is no-op for MongoDB since operations are immediate
    Task<int> IDataContext.SaveChanges(CancellationToken cancellationToken)
    {
        return Task.FromResult(0);
    }

    // Get entity set for the given entity type, using type name as collection name
    IEntitySet<TEntity> IDataContext.Set<TEntity>() where TEntity : class
    {
        var collection = database.GetCollection<TEntity>(typeof(TEntity).Name);
        return new MongoEntitySet<TEntity>(collection);
    }
}
