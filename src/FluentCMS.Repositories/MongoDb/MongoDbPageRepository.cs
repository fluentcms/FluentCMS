using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDb;

public class MongoDbPageRepository : MongoDbGenericRepository<Page>, IPageRepository
{
    public MongoDbPageRepository(IMongoDBContext mongoDbContext) : base(mongoDbContext)
    {
    }

    public Task<IEnumerable<Page>> GetByParentId(Guid id)
    {
        // TODO: implement here
        return Task.FromResult<IEnumerable<Page>>([]);
    }

    public Task<Page> GetByPath(string path)
    {
        // TODO: implement here
        return Task.FromResult<Page>(null);
    }

    public async Task<IEnumerable<Page>> GetBySiteId(Guid siteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<Page>.Filter.Eq(x => x.SiteId, siteId);

        var findResult = await Collection.FindAsync(filter, null, cancellationToken);

        return findResult.ToEnumerable(cancellationToken);
    }

    public Task<IEnumerable<Page>> GetBySiteIdAndParentId(Guid siteId, Guid? parentId = null)
    {
        throw new NotImplementedException();
    }
}
