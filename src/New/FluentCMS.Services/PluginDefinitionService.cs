namespace FluentCMS.Services;

public interface IPluginDefinitionService : IService
{
    Task<IEnumerable<PluginDefinition>> GetAll(CancellationToken cancellationToken = default);
    Task<PluginDefinition> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<PluginDefinition> Create(PluginDefinition pluginDefinition, CancellationToken cancellationToken = default);
    Task<PluginDefinition> Update(PluginDefinition pluginDefinition, CancellationToken cancellationToken = default);
    Task<PluginDefinition> Delete(Guid id, CancellationToken cancellationToken = default);
}


public class PluginDefinitionService(IPluginDefinitionRepository pluginDefinitionRepository) : IPluginDefinitionService
{
    public async Task<PluginDefinition> Create(PluginDefinition pluginDefinition, CancellationToken cancellationToken = default)
    {
        return await pluginDefinitionRepository.Create(pluginDefinition, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginDefinitionUnableToCreate);
    }

    public async Task<PluginDefinition> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        return await pluginDefinitionRepository.Delete(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginDefinitionUnableToDelete);
    }

    public async Task<PluginDefinition> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return await pluginDefinitionRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginDefinitionNotFound);
    }

    public async Task<IEnumerable<PluginDefinition>> GetAll(CancellationToken cancellationToken = default)
    {
        return await pluginDefinitionRepository.GetAll(cancellationToken);
    }

    public async Task<PluginDefinition> Update(PluginDefinition pluginDefinition, CancellationToken cancellationToken = default)
    {
        return await pluginDefinitionRepository.Update(pluginDefinition, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginDefinitionUnableToUpdate);
    }
}
