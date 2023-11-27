using FluentCMS.Entities;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

public class LayoutRepository(
    IMongoDBContext mongoDbContext,
    IApplicationContext applicationContext) :
    GenericRepository<Layout>(mongoDbContext, applicationContext), ILayoutRepository
{
    public async Task<IEnumerable<Layout>> GetAll(Guid siteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var siteIdFilter = Builders<Layout>.Filter.Eq(x => x.SiteId, siteId);

        var layouts = await Collection.FindAsync(siteIdFilter, null, cancellationToken);

        return layouts.ToEnumerable(cancellationToken);
    }
}
