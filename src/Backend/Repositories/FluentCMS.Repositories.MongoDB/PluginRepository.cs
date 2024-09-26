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
