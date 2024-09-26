namespace FluentCMS.Repositories.LiteDb;

public class PluginRepository(ILiteDBContext liteDbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<Plugin>(liteDbContext, apiExecutionContext), IPluginRepository
{
    public async Task<IEnumerable<Plugin>> GetByPageId(Guid pageId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Collection.Query().Where(x => x.PageId == pageId).ToListAsync();
    }

    public async Task<Plugin> UpdateOrder(Guid pluginId, string section, int order, CancellationToken cancellationToken = default)
    {
        var plugin = await GetById(pluginId, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginNotFound);

        plugin.Order = order;
        plugin.Section = section;

        return await Update(plugin, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginUnableToUpdate);
    }
}
