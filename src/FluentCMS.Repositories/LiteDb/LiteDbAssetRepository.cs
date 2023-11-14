using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Repositories.LiteDb;

public class LiteDbAssetRepository : LiteDbGenericRepository<Asset>, IAssetRepository
{
    public LiteDbAssetRepository(LiteDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<IEnumerable<Asset>> GetAllOfSite(Guid siteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var data = await Collection.FindAsync(x => x.SiteId == siteId);
        return data;
    }

    public async Task<Asset?> GetAssetByName(Guid siteId, Guid? folderId, string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var data = await Collection.FindOneAsync(x => x.SiteId == siteId && x.FolderId == folderId && x.Name == name);
        return data;
    }
}
