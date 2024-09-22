namespace FluentCMS.Repositories.Caching;

public class PluginRepository(IPluginRepository repository, ICacheProvider cacheProvider) : SiteAssociatedRepository<Plugin>(repository, cacheProvider), IPluginRepository
{
    public async Task<IEnumerable<Plugin>> GetByPageId(Guid pageId, CancellationToken cancellationToken = default)
    {
        var entities = await GetAll(cancellationToken);
        return entities.Where(x => x.PageId == pageId);
    }
}
