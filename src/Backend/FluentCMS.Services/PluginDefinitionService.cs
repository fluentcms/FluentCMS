using FluentCMS.Providers.MessageBusProviders;

namespace FluentCMS.Services;

public interface IPluginDefinitionService : IAutoRegisterService
{
    Task<IEnumerable<PluginDefinition>> GetAll(CancellationToken cancellationToken = default);
    Task<PluginDefinition> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<PluginDefinition> Create(PluginDefinition pluginDefinition, CancellationToken cancellationToken = default);
    Task<PluginDefinition> Update(PluginDefinition pluginDefinition, CancellationToken cancellationToken = default);
    Task<PluginDefinition> Delete(Guid id, CancellationToken cancellationToken = default);
}


public class PluginDefinitionService(IPluginDefinitionRepository pluginDefinitionRepository, IMessagePublisher messagePublisher) : IPluginDefinitionService
{
    public async Task<PluginDefinition> Create(PluginDefinition pluginDefinition, CancellationToken cancellationToken = default)
    {
        var created = await pluginDefinitionRepository.Create(pluginDefinition, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginDefinitionUnableToCreate);

        await messagePublisher.Publish(new Message<PluginDefinition>(ActionNames.PluginDefinitionCreated, created), cancellationToken);

        return created;
    }

    public async Task<PluginDefinition> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var deleted = await pluginDefinitionRepository.Delete(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginDefinitionUnableToDelete);

        await messagePublisher.Publish(new Message<PluginDefinition>(ActionNames.PluginDefinitionDeleted, deleted), cancellationToken);

        return deleted;
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
        var updated = await pluginDefinitionRepository.Update(pluginDefinition, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginDefinitionUnableToUpdate);

        await messagePublisher.Publish(new Message<PluginDefinition>(ActionNames.PluginDefinitionUpdated, updated), cancellationToken);

        return updated;
    }
}
