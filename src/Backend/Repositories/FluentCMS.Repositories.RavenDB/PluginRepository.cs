namespace FluentCMS.Repositories.RavenDB;

public class PluginRepository(IRavenDBContext RavenDbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<Plugin>(RavenDbContext, apiExecutionContext), IPluginRepository
{
    public async Task<IEnumerable<Plugin>> GetByPageId(Guid pageId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            var qres = await session.Query<RavenEntity<Plugin>>()
                                    .Where(x => x.Data.PageId == pageId)
                                    .Select(x => x.Data)
                                    .ToListAsync(cancellationToken);

            return qres.AsEnumerable();
        }
    }

    public async Task<Plugin?> UpdateOrder(Guid pluginId, string section, int order, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            var plugin = await session.Query<RavenEntity<Plugin>>().SingleOrDefaultAsync(x => x.Data.Id == pluginId, cancellationToken);

            if (plugin != null)
            {
                plugin.Data.Order = order;
                plugin.Data.Section = section;

                await session.SaveChangesAsync();

                return plugin.Data;
            }
        }

        return default;
    }

    public async Task<Plugin?> UpdateCols(Guid pluginId, int cols, int colsMd, int colsLg, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            var plugin = await session.Query<RavenEntity<Plugin>>().SingleOrDefaultAsync(x => x.Data.Id == pluginId, cancellationToken);

            if (plugin != null)
            {
                plugin.Data.Cols = cols;
                plugin.Data.ColsMd = colsMd;
                plugin.Data.ColsLg = colsLg;

                await session.SaveChangesAsync();

                return plugin.Data;
            }
        }

        return default;
    }
}
