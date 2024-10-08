namespace FluentCMS.Services;

public interface IPluginService : IAutoRegisterService
{
    Task<IEnumerable<Plugin>> GetByPageId(Guid pageId, CancellationToken cancellationToken = default);
    Task<Plugin> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<Plugin> Create(Plugin plugin, CancellationToken cancellationToken = default);
    Task<Plugin> InitialCreate(Guid pageId, Guid pluginDefinitionId, string section, CancellationToken cancellationToken = default);
    Task<Plugin> Update(Plugin plugin, CancellationToken cancellationToken = default);
    Task<IEnumerable<Plugin>> UpdateOrders(IEnumerable<PluginOrder> pluginOrders, CancellationToken cancellationToken = default);
    Task<Plugin> UpdateCols(Guid pluginId, int cols, int colsMd, int colsLg, CancellationToken cancellationToken = default);
    Task<Plugin> Delete(Guid id, CancellationToken cancellationToken = default);
}


public class PluginService(IPluginRepository pluginRepository, IPageRepository pageRepository, ISettingsRepository settingsRepository, IPermissionManager permissionManager, IMessagePublisher messagePublisher) : IPluginService
{
    public async Task<Plugin> Create(Plugin plugin, CancellationToken cancellationToken = default)
    {
        //if (!await permissionManager.HasAccess(page, PermissionActionNames.PageContributor, cancellationToken))
        //    throw new AppException(ExceptionCodes.PermissionDenied);

        var created = await pluginRepository.Create(plugin, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginUnableToCreate);

        var settings = new Dictionary<string, string> {};
        await settingsRepository.Update(created.Id, settings, cancellationToken);

        await messagePublisher.Publish(new Message<Plugin>(ActionNames.PluginCreated, created), cancellationToken);

        return created;
    }

    public async Task<Plugin> InitialCreate(Guid pageId, Guid pluginDefinitionId, string section, CancellationToken cancellationToken = default)
    {
        var page = await pageRepository.GetById(pageId, cancellationToken) ??
            throw new AppException(ExceptionCodes.PageNotFound);

        var plugin = new Plugin
        {
            PageId = pageId,
            DefinitionId = pluginDefinitionId,
            SiteId = page.SiteId,
            Section = string.IsNullOrWhiteSpace(section) ? "main" : section,
            Cols = 12
        };

        return await Create(plugin, cancellationToken);
    }

    public async Task<Plugin> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var plugin = await pluginRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginNotFound);

        //if (!await permissionManager.HasAccess(plugin, PermissionActionNames.PluginContributor, cancellationToken))
        //    throw new AppException(ExceptionCodes.PermissionDenied);

        var deleted = await pluginRepository.Delete(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginUnableToDelete);

        await messagePublisher.Publish(new Message<Plugin>(ActionNames.PluginDeleted, deleted), cancellationToken);

        return plugin;
    }

    public async Task<Plugin> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var plugin = await pluginRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginNotFound);

        //if (!await permissionManager.HasAccess(plugin, PermissionActionNames.PluginView, cancellationToken))
        //    throw new AppException(ExceptionCodes.PermissionDenied);

        return plugin;
    }

    public async Task<IEnumerable<Plugin>> GetByPageId(Guid pageId, CancellationToken cancellationToken = default)
    {
        var pagePlugins = await pluginRepository.GetByPageId(pageId, cancellationToken);

        //var plugins = await permissionManager.HasAccess(pagePlugins, PermissionActionNames.PluginView, cancellationToken);

        return pagePlugins;
    }

    public async Task<Plugin> Update(Plugin plugin, CancellationToken cancellationToken = default)
    {
        //if (!await permissionManager.HasAccess(plugin, PermissionActionNames.PluginContributor, cancellationToken))
        //    throw new AppException(ExceptionCodes.PermissionDenied);

        var updated = await pluginRepository.Update(plugin, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginUnableToUpdate);

        await messagePublisher.Publish(new Message<Plugin>(ActionNames.PluginUpdated, updated), cancellationToken);

        return updated;
    }

    public async Task<IEnumerable<Plugin>> UpdateOrders(IEnumerable<PluginOrder> pluginOrders, CancellationToken cancellationToken = default)
    {
        var order = 0;
        var plugins = new List<Plugin>();
        foreach (var pluginOrder in pluginOrders)
        {
            var plugin = await pluginRepository.UpdateOrder(pluginOrder.Id, pluginOrder.Section, order, cancellationToken);
            if (plugin != null)
            {
                plugins.Add(plugin);
                order++;
            }
        }
        return plugins;
    }

    public async Task<Plugin> UpdateCols(Guid pluginId, int cols, int colsMd, int colsLg, CancellationToken cancellationToken = default)
    {
        return await pluginRepository.UpdateCols(pluginId, cols, colsMd, colsLg, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginUnableToUpdateCols);
    }

}
