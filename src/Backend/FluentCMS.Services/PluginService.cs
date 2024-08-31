namespace FluentCMS.Services;

public interface IPluginService : IAutoRegisterService
{
    Task<IEnumerable<Plugin>> GetByPageId(Guid pageId, CancellationToken cancellationToken = default);
    Task<Plugin> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<Plugin> Create(Plugin plugin, CancellationToken cancellationToken = default);
    Task<Plugin> Update(Plugin plugin, CancellationToken cancellationToken = default);
    Task<Plugin> Delete(Guid id, CancellationToken cancellationToken = default);
}


public class PluginService(IPluginRepository pluginRepository, IPageRepository pageRepository, IPermissionManager permissionManager) : IPluginService
{
    public async Task<Plugin> Create(Plugin plugin, CancellationToken cancellationToken = default)
    {
        var page = await pageRepository.GetById(plugin.PageId, cancellationToken) ??
            throw new AppException(ExceptionCodes.PageNotFound);

        if (!await permissionManager.HasAccess(page, PermissionActionNames.PageContributor, cancellationToken))
            throw new AppException(ExceptionCodes.PermissionDenied);

        return await pluginRepository.Create(plugin, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginUnableToCreate);
    }

    public async Task<Plugin> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var plugin = await pluginRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginNotFound);

        if (!await permissionManager.HasAccess(plugin, PermissionActionNames.PluginContributor, cancellationToken))
            throw new AppException(ExceptionCodes.PermissionDenied);

        _ = await pluginRepository.Delete(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginUnableToDelete);

        return plugin;
    }

    public async Task<Plugin> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var plugin = await pluginRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginNotFound);

        if (!await permissionManager.HasAccess(plugin, PermissionActionNames.PluginView, cancellationToken))
            throw new AppException(ExceptionCodes.PermissionDenied);

        return plugin;
    }

    public async Task<IEnumerable<Plugin>> GetByPageId(Guid pageId, CancellationToken cancellationToken = default)
    {
        var pagePlugins = await pluginRepository.GetByPageId(pageId, cancellationToken);

        var plugins = await permissionManager.HasAccess(pagePlugins, PermissionActionNames.PluginView, cancellationToken);

        return plugins;
    }

    public async Task<Plugin> Update(Plugin plugin, CancellationToken cancellationToken = default)
    {
        if (!await permissionManager.HasAccess(plugin, PermissionActionNames.PluginContributor, cancellationToken))
            throw new AppException(ExceptionCodes.PermissionDenied);

        return await pluginRepository.Update(plugin, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginUnableToUpdate);
    }
}
