using FluentCMS.Entities;
using FluentCMS.Repositories;

namespace FluentCMS.Services;

public interface IPluginDefinitionService
{
    Task<IEnumerable<PluginDefinition>> GetAll(CancellationToken cancellationToken = default);
    Task<PluginDefinition> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<PluginDefinition> Create(PluginDefinition pluginDefinition, CancellationToken cancellationToken = default);
    Task<PluginDefinition> Update(PluginDefinition pluginDefinition, CancellationToken cancellationToken = default);
    Task Delete(Guid id, CancellationToken cancellationToken = default);
}


public class PluginDefinitionService(
    IPluginDefinitionRepository pluginDefinitionRepository) : IPluginDefinitionService
{
    public async Task<PluginDefinition> Create(PluginDefinition pluginDefinition, CancellationToken cancellationToken = default)
    {
        var newPluginDef = await pluginDefinitionRepository.Create(pluginDefinition, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginDefinitionUnableToCreate);

        return newPluginDef;
    }

    public async Task Delete(Guid id, CancellationToken cancellationToken = default)
    {
        _ = await pluginDefinitionRepository.GetById(id, cancellationToken) ??
           throw new AppException(ExceptionCodes.PluginNotFound);

        _ = await pluginDefinitionRepository.Delete(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginUnableToDelete);
    }

    public async Task<PluginDefinition> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        //fetch page from db
        var pluginDef = await pluginDefinitionRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginNotFound);

        return pluginDef;
    }

    public async Task<IEnumerable<PluginDefinition>> GetAll(CancellationToken cancellationToken = default)
    {
        //fetch page from db
        return await pluginDefinitionRepository.GetAll(cancellationToken);
    }

    public async Task<PluginDefinition> Update(PluginDefinition pluginDefinition, CancellationToken cancellationToken = default)
    {
        //fetch page from db
        _ = await pluginDefinitionRepository.GetById(pluginDefinition.Id, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginNotFound);

        //save changes
        var updatedPluginDef = await pluginDefinitionRepository.Update(pluginDefinition, cancellationToken) ??
            throw new AppException(ExceptionCodes.PluginUnableToUpdate);

        return updatedPluginDef;
    }
}
