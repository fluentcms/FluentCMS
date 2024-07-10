namespace FluentCMS.Services;

public interface IPluginContentService : IAutoRegisterService
{
    Task<PluginContent> Create(PluginContent pluginContent, CancellationToken cancellationToken = default);
    Task<PluginContent> Delete(string pluginContentTypeName, Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<PluginContent>> GetByPluginId(Guid pluginId, CancellationToken cancellationToken = default);
    Task<PluginContent> GetById(string pluginContentTypeName, Guid id, CancellationToken cancellationToken = default);
    Task<PluginContent> Update(PluginContent pluginContent, CancellationToken cancellationToken);
}

public class PluginContentService(IPluginContentRepository repository) : IPluginContentService
{
    public async Task<PluginContent> Create(PluginContent pluginContent, CancellationToken cancellationToken = default)
    {
        return await repository.Create(pluginContent, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginContentUnableToCreate);
    }

    public async Task<PluginContent> Delete(string pluginContentTypeName, Guid id, CancellationToken cancellationToken = default)
    {
        return await repository.Delete(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginContentUnableToDelete);
    }

    public async Task<PluginContent> GetById(string pluginContentTypeName, Guid id, CancellationToken cancellationToken = default)
    {
        return await repository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginContentNotFound);
    }

    public async Task<IEnumerable<PluginContent>> GetByPluginId(Guid pluginId, CancellationToken cancellationToken = default)
    {
        return await repository.GetByPluginId(pluginId, cancellationToken);
    }

    public async Task<PluginContent> Update(PluginContent pluginContent, CancellationToken cancellationToken)
    {
        return await repository.Update(pluginContent, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginContentUnableToUpdate);
    }
}
