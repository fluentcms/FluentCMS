using FluentCMS.Entities;
using MongoDB.Driver;
using System.Threading;

namespace FluentCMS.Repositories.MongoDB;

public class PageRepository : GenericRepository<Page>, IPageRepository
{
    public PageRepository(IMongoDBContext mongoDbContext) : base(mongoDbContext)
    {
    }

    public async Task<Page> GetByPath(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<Page>.Filter.Eq(x => x.Path, path);

        var findResult = await Collection.FindAsync(filter, null, cancellationToken);

        return findResult.SingleOrDefault(cancellationToken);
    }

    public async Task<IEnumerable<Page>> GetBySiteId(Guid siteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<Page>.Filter.Eq(x => x.SiteId, siteId);

        var findResult = await Collection.FindAsync(filter, null, cancellationToken);

        return findResult.ToEnumerable(cancellationToken);
    }
}
