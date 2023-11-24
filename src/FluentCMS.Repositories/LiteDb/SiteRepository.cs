using FluentCMS.Entities;

namespace FluentCMS.Repositories.LiteDb;

public class SiteRepository : GenericRepository<Site>, ISiteRepository
{
    public SiteRepository(LiteDbContext dbContext, IApplicationContext applicationContext) : base(dbContext, applicationContext)
    {
    }

    public async Task<Site?> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Collection.FindOneAsync(x => x.Urls.Contains(url)) ?? default;
    }
}
