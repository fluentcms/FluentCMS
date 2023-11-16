using FluentCMS.Entities;

namespace FluentCMS.Repositories.LiteDb;

public class LiteDbSiteRepository : LiteDbGenericRepository<Site>, ISiteRepository
{
    public LiteDbSiteRepository(LiteDbContext dbContext) : base(dbContext)
    {
    }

    public Task<bool> CheckUrls(IList<string> urls, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public async Task<Site?> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Collection.FindOneAsync(x => x.Urls.Contains(url)) ?? default;
    }
}
