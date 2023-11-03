using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Repositories.LiteDb;

public class LiteDbSiteRepository : LiteDbGenericRepository<Site>, ISiteRepository
{
    public LiteDbSiteRepository(LiteDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<Site?> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Collection.FindOneAsync(x => x.Urls.Contains(url)) ?? default;
    }
}
