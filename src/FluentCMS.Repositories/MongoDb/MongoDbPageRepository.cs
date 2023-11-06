using FluentCMS.Entities.Pages;
using FluentCMS.Repositories.Abstractions;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDb;

public class MongoDbPageRepository : MongoDbGenericRepository<Page>, IPageRepository
{
    public MongoDbPageRepository(IMongoDBContext mongoDbContext) : base(mongoDbContext)
    {
    }

    public Task<Page> GetByPath(string path)
    {
        throw new NotImplementedException();
    }

    public async Task<IEnumerable<Page>> GetBySiteId(Guid siteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<Page>.Filter.Eq(x => x.SiteId, siteId);

        var findResult = await Collection.FindAsync(filter, null, cancellationToken);

        return findResult.ToEnumerable(cancellationToken);
    }
}
