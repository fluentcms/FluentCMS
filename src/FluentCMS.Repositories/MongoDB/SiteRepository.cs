using FluentCMS.Entities;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

public class SiteRepository(
    IMongoDBContext mongoDbContext,
    IApplicationContext applicationContext) :
    SiteAssociatedRepository<Site>(mongoDbContext, applicationContext),
    ISiteRepository
{
    public async Task<Site?> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<Site>.Filter.AnyEq(x => x.Urls, url);

        var findResult = await Collection.FindAsync(filter, null, cancellationToken);

        return await findResult.SingleOrDefaultAsync(cancellationToken);
    }
}
