namespace FluentCMS.Repositories.LiteDb;

public class PluginContentRepository : SiteAssociatedRepository<PluginContent>, IPluginContentRepository
{
    public PluginContentRepository(ILiteDBContext liteDbContext, IAuthContext authContext) : base(liteDbContext, authContext)
    {
    }

    public async Task<IEnumerable<PluginContent>> GetByPluginId(Guid pluginId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Query().Where(x => x.PluginId == pluginId).ToEnumerableAsync();
    }
}
