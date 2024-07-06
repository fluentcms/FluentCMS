namespace FluentCMS.Repositories.MongoDB;

public class PluginContentRepository : SiteAssociatedRepository<PluginContent>, IPluginContentRepository
{
    public PluginContentRepository(IMongoDBContext mongoDbContext, IAuthContext authContext) : base(mongoDbContext, authContext)
    {
    }

    public async Task<IEnumerable<PluginContent>> GetByPluginId(Guid pluginId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Find(x => x.PluginId == pluginId).ToListAsync(cancellationToken: cancellationToken);
    }
}
