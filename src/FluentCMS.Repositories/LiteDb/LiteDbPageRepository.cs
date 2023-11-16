using FluentCMS.Entities;

namespace FluentCMS.Repositories.LiteDb;

public class LiteDbPageRepository : LiteDbGenericRepository<Page>, IPageRepository
{
    public LiteDbPageRepository(LiteDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<IEnumerable<Page>> GetBySiteId(Guid siteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Collection.FindAsync(x => x.SiteId == siteId);
    }

    public async Task<Page> GetByPath(string path)
    {
        return await Collection.FindOneAsync(x => x.Path == path);
    }
}
