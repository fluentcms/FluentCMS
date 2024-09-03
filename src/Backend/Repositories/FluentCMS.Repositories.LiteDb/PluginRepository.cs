namespace FluentCMS.Repositories.LiteDb;

public class PluginRepository : SiteAssociatedRepository<Plugin>, IPluginRepository
{
    public PluginRepository(ILiteDBContext liteDbContext, IApiExecutionContext apiExecutionContext) : base(liteDbContext, apiExecutionContext)
    {
    }

    public async Task<IEnumerable<Plugin>> GetByPageId(Guid pageId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Collection.Query().Where(x => x.PageId == pageId).ToListAsync();
    }
}
