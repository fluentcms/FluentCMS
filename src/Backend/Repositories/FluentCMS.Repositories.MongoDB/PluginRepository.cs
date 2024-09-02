namespace FluentCMS.Repositories.MongoDB;

public class PluginRepository : SiteAssociatedRepository<Plugin>, IPluginRepository
{
    public PluginRepository(IMongoDBContext mongoDbContext, IApiExecutionContext apiExecutionContext) : base(mongoDbContext, apiExecutionContext)
    {
    }

    public async Task<IEnumerable<Plugin>> GetByPageId(Guid pageId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<Plugin>.Filter.Eq(x => x.PageId, pageId);
        var result = await Collection.FindAsync(filter, cancellationToken: cancellationToken);
        return await result.ToListAsync(cancellationToken);
    }
}
