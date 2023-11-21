using FluentCMS.Entities;

namespace FluentCMS.Repositories.LiteDb;

public class PageRepository : GenericRepository<Page>, IPageRepository
{
    public PageRepository(LiteDbContext dbContext, IApplicationContext applicationContext) : base(dbContext, applicationContext)
    {
    }

    public async Task<IEnumerable<Page>> GetBySiteId(Guid siteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Collection.FindAsync(x => x.SiteId == siteId);
    }

    public async Task<Page> GetByPath(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Collection.FindOneAsync(x => x.Path == path);
    }
}
