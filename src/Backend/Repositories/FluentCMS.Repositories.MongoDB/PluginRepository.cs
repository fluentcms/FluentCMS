namespace FluentCMS.Repositories.MongoDB;

public class PluginRepository(IMongoDBContext mongoDbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<Plugin>(mongoDbContext, apiExecutionContext), IPluginRepository
{
    public async Task<IEnumerable<Plugin>> GetByPageId(Guid pageId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<Plugin>.Filter.Eq(x => x.PageId, pageId);
        var result = await Collection.FindAsync(filter, cancellationToken: cancellationToken);
        return await result.ToListAsync(cancellationToken);
    }

    public async Task<Plugin?> UpdateOrder(Guid pluginId, string section, int order, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var plugin = await GetById(pluginId, cancellationToken);

        if (plugin != null)
        {
            plugin.Order = order;
            plugin.Section = section;

            return await Update(plugin, cancellationToken);
        }
        return default;
    }

    public async Task<Plugin?> UpdateCols(Guid pluginId, int cols, int colsMd, int colsLg, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var plugin = await GetById(pluginId, cancellationToken);

        if (plugin != null)
        {
            plugin.Cols = cols;
            plugin.ColsMd = colsMd;
            plugin.ColsLg = colsLg;
            return await Update(plugin, cancellationToken);
        }
        return default;
    }
}
