namespace FluentCMS.Repositories.RavenDB;

public class PluginRepository(IRavenDBContext RavenDbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<Plugin>(RavenDbContext, apiExecutionContext), IPluginRepository
{
    public async Task<IEnumerable<Plugin>> GetByPageId(Guid pageId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            var qres = await session.Query<Plugin>().Where(x => x.PageId == pageId).ToListAsync(cancellationToken);

            return qres.AsEnumerable();
        }
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

    public async Task<Plugin?> UpdateSettings(Guid pluginId, Dictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var plugin = await GetById(pluginId, cancellationToken);

        if (plugin != null)
        {
            plugin.Settings = settings;
            return await Update(plugin, cancellationToken);
        }
        return default;
    }
}
