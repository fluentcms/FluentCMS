namespace FluentCMS.Repositories.MongoDB;

public class PluginContentRepository : SiteAssociatedRepository<PluginContent>, IPluginContentRepository
{
    public PluginContentRepository(IMongoDBContext mongoDbContext, IApiExecutionContext apiExecutionContext) : base(mongoDbContext, apiExecutionContext)
    {
    }

    public async Task<IEnumerable<PluginContent>> GetByPluginId(Guid pluginId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Find(x => x.PluginId == pluginId).ToListAsync(cancellationToken: cancellationToken);
    }
}
