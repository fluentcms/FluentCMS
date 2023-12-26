namespace FluentCMS.Repositories.MongoDB;

public class SiteRepository : EntityRepository<Site>, ISiteRepository
{
    public SiteRepository(IMongoDBContext mongoDbContext) : base(mongoDbContext)
    {
    }
    public async Task<Site?> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<Site>.Filter.AnyEq(x => x.Urls, url);
        var findResult = await Collection.FindAsync(filter, null, cancellationToken);

        return await findResult.SingleOrDefaultAsync(cancellationToken);
    }
}
