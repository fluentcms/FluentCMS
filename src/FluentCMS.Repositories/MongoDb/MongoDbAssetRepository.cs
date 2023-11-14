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

    public async Task<Asset?> GetAssetByName(Guid siteId, Guid? folderId, string name, CancellationToken cancellationToken = default)
    {
        var filter = Builders<Asset>.Filter.And(
            Builders<Asset>.Filter.Eq(x => x.SiteId, siteId),
            Builders<Asset>.Filter.Eq(x => x.FolderId, folderId),
            Builders<Asset>.Filter.Eq(x => x.Name, name));
        var findResult = await Collection.FindAsync(filter, null, cancellationToken);
        return await findResult.FirstOrDefaultAsync(cancellationToken);
    }
}
