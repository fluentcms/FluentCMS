using FluentCMS.Entities;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

public class SiteRepository(
    IMongoDBContext mongoDbContext,
    IApplicationContext applicationContext) :
    SiteAssociatedRepository<Site>(mongoDbContext, applicationContext),
    ISiteRepository
{
    public override async Task<Site?> Create(Site entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var newSite = await base.Create(entity, cancellationToken);

        if (newSite == null)
            return null;

        newSite.SiteId = newSite.Id;

        return await Update(newSite, cancellationToken);
    }

    public async Task<Site?> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<Site>.Filter.AnyEq(x => x.Urls, url);

        var findResult = await Collection.FindAsync(filter, null, cancellationToken);

        return await findResult.SingleOrDefaultAsync(cancellationToken);
    }
}
