using FluentCMS.Entities.Sites;

namespace FluentCMS.Repository.LiteDb;
public class LiteDbSiteRepository : LiteDbGenericRepository<Site>, ISiteRepository
{
    public LiteDbSiteRepository(LiteDbContext dbContext) : base(dbContext)
    {
    }

    public Task<Site> GetByUrl(string url)
    {
        return Collection.FindOneAsync(x => x.Urls.Contains(url));
    }
}
