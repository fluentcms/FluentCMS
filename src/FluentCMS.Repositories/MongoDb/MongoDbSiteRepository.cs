using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDb;

public class MongoDbSiteRepository(IMongoDBContext mongoDbContext) : MongoDbGenericRepository<Site>(mongoDbContext), ISiteRepository
{
    public async Task<bool> CheckUrls(IList<string> urls, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var builder = Builders<Site>.Filter;
        var filters = new List<FilterDefinition<Site>>();

        foreach (var url in urls)
            filters.Add(builder.AnyEq(x => x.Urls, url));

        var findResult = await Collection.FindAsync(Builders<Site>.Filter.Or(filters), null, cancellationToken);

        return findResult.Any(cancellationToken);
    }

    public async Task<Site?> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<Site>.Filter.AnyEq(x => x.Urls, url);

        var findResult = await Collection.FindAsync(filter, null, cancellationToken);

        return await findResult.SingleOrDefaultAsync(cancellationToken);
    }
}
