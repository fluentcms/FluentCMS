namespace FluentCMS.Services;

public interface IPluginService : IAutoRegisterService
{
    Task<IEnumerable<Plugin>> GetByColumnId(Guid columnId, CancellationToken cancellationToken = default);
    Task<Plugin> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<Plugin> Create(Plugin plugin, CancellationToken cancellationToken = default);
    Task<Plugin> Update(Plugin plugin, CancellationToken cancellationToken = default);
    Task<Plugin> Delete(Guid id, CancellationToken cancellationToken = default);
}


public class PluginService(IPluginRepository pluginRepository) : IPluginService
{
    public async Task<Plugin> Create(Plugin plugin, CancellationToken cancellationToken = default)
    {
        return await pluginRepository.Create(plugin, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginUnableToCreate);
    }

    public async Task<Plugin> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        return await pluginRepository.Delete(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginUnableToDelete);
    }

    public async Task<Plugin> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return await pluginRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginNotFound);
    }

    public async Task<IEnumerable<Plugin>> GetByColumnId(Guid columnId, CancellationToken cancellationToken = default)
    {
        return await pluginRepository.GetByColumnId(columnId, cancellationToken);
    }

    public async Task<Plugin> Update(Plugin plugin, CancellationToken cancellationToken = default)
    {
        return await pluginRepository.Update(plugin, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginUnableToUpdate);
    }
}
