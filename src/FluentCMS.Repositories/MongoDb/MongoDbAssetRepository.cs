using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Repositories.MongoDb;

internal class MongoDbAssetRepository : MongoDbGenericRepository<Asset>, IAssetRepository
{
    public MongoDbAssetRepository(IMongoDBContext mongoDbContext) : base(mongoDbContext)
    {
    }
}
