using FluentCMS.Entities;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

public class PageRepository(
    IMongoDBContext mongoDbContext,
    IApplicationContext applicationContext) :
    SiteAssociatedRepository<Page>(mongoDbContext, applicationContext),
    IPageRepository
{

    public async Task<Page> GetByPath(Guid siteId, string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<Page>.Filter.Eq(x => x.Path, path);
        filter &= Builders<Page>.Filter.Eq(x => x.SiteId, siteId);

        var findResult = await Collection.FindAsync(filter, null, cancellationToken);

        return findResult.SingleOrDefault(cancellationToken);
    }
}
