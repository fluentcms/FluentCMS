using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDb;

internal class MongoDbAssetRepository : MongoDbGenericRepository<Asset>, IAssetRepository
{
    public MongoDbAssetRepository(IMongoDBContext mongoDbContext) : base(mongoDbContext)
    {
    }

    public async Task<IEnumerable<Asset>> GetAllOfSite(Guid siteId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<Asset>.Filter.Eq(x => x.SiteId, siteId);
        var findResult = await Collection.FindAsync(filter, null, cancellationToken);
        return findResult.ToEnumerable(cancellationToken);
    }
}
