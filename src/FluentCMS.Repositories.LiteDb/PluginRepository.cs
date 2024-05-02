using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;
using LiteDB;

namespace FluentCMS.Repositories.LiteDb;

public class PluginRepository : SiteAssociatedRepository<Plugin>, IPluginRepository
{
    public PluginRepository(ILiteDBContext liteDbContext, IAuthContext authContext) : base(liteDbContext, authContext)
    {
    }

    public async Task<IEnumerable<Plugin>> GetByPageId(Guid pageId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Query.EQ(nameof(Plugin.PageId), pageId);
        var result = await Collection.FindAsync(filter);
        return result.ToList();
    }
}
