using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;

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

    public async Task<IEnumerable<Page>> GetByParentId(Guid parentId)
    {
        return await Collection.FindAsync(x => x.ParentId == parentId);
    }

    public async Task<IEnumerable<Page>> GetBySiteIdAndParentId(Guid siteId, Guid? parentId = null)
    {
        return await Collection.FindAsync(x => x.ParentId == parentId && x.SiteId == siteId);
    }
}
