namespace FluentCMS.Repositories.Caching;

public class PluginRepository(IPluginRepository repository, ICacheProvider cacheProvider) : SiteAssociatedRepository<Plugin>(repository, cacheProvider), IPluginRepository
{
    public async Task<IEnumerable<Plugin>> GetByPageId(Guid pageId, CancellationToken cancellationToken = default)
    {
        var entities = await GetAll(cancellationToken);
        return entities.Where(x => x.PageId == pageId);
    }

    public async Task<Plugin?> UpdateOrder(Guid pluginId, string section, int order, CancellationToken cancellationToken = default)
    {
        var plugin = await repository.UpdateOrder(pluginId, section, order, cancellationToken);
        InvalidateCache();
        return plugin;
    }
    public async Task<Plugin?> UpdateCols(Guid pluginId, int cols, int colsMd, int colsLg, CancellationToken cancellationToken = default)
    {
        var plugin = await repository.UpdateCols(pluginId, cols, colsMd, colsLg, cancellationToken);
        InvalidateCache();
        return plugin;
    }
    public async Task<Plugin?> UpdateSettings(Guid pluginId, Dictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        var plugin = await repository.UpdateSettings(pluginId, settings, cancellationToken);
        InvalidateCache();
        return plugin;
    }
}
