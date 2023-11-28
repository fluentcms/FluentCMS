using FluentCMS.Entities;
using FluentCMS.Repositories;

namespace FluentCMS.Services;

public interface IPluginService
{
    Task<IEnumerable<Plugin>> GetByPageId(Guid pageId, CancellationToken cancellationToken = default);
    Task<Plugin> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<Plugin> Create(Plugin plugin, CancellationToken cancellationToken = default);
    Task<Plugin> Update(Plugin plugin, CancellationToken cancellationToken = default);
    Task Delete(Guid id, CancellationToken cancellationToken = default);
}


public class PluginService(
    IPluginRepository pluginRepository) : IPluginService
{
    public async Task<Plugin> Create(Plugin plugin, CancellationToken cancellationToken = default)
    {
        var newPlugin = await pluginRepository.Create(plugin, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginUnableToCreate);

        return newPlugin;
    }

    public async Task Delete(Guid id, CancellationToken cancellationToken = default)
    {
        _ = await pluginRepository.GetById(id, cancellationToken) ??
           throw new AppException(ExceptionCodes.PluginNotFound);

        _ = await pluginRepository.Delete(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginUnableToDelete);
    }

    public async Task<Plugin> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        //fetch page from db
        var plugin = await pluginRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginNotFound);

        return plugin;
    }

    public async Task<IEnumerable<Plugin>> GetByPageId(Guid pageId, CancellationToken cancellationToken = default)
    {
        //fetch page from db
        return await pluginRepository.GetByPageId(pageId, cancellationToken);
    }

    public async Task<Plugin> Update(Plugin plugin, CancellationToken cancellationToken = default)
    {
        //fetch page from db
        var existingPlugin = await pluginRepository.GetById(plugin.Id, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginNotFound);

        //save changes
        var updatedPlugin = await pluginRepository.Update(existingPlugin, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginUnableToUpdate);

        return updatedPlugin;
    }
}
