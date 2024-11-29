namespace FluentCMS.Services;

public interface ISettingsService : IAutoRegisterService
{
    Task<IEnumerable<Settings>> GetByIds(IEnumerable<Guid> entityIds, CancellationToken cancellationToken = default);
    Task<Settings> GetById(Guid entityId, CancellationToken cancellationToken = default);
    Task<Settings> Update(Guid entityId, Dictionary<string, string> values, CancellationToken cancellationToken = default);
}

public class SettingsService(ISettingsRepository settingsRepository, IMessagePublisher messagePublisher) : ISettingsService
{
    public async Task<Settings> GetById(Guid entityId, CancellationToken cancellationToken = default)
    {
        return await settingsRepository.GetById(entityId, cancellationToken) ?? new Settings { Id = entityId, Values = [] };
    }

    public async Task<IEnumerable<Settings>> GetByIds(IEnumerable<Guid> entityIds, CancellationToken cancellationToken = default)
    {
        var result = await settingsRepository.GetByIds(entityIds, cancellationToken);
        // check if some ids are missing, then add them with empty values
        var missingIds = entityIds.Except(result.Select(x => x.Id));
        foreach (var id in missingIds)
            result = result.Append(new Settings { Id = id, Values = [] });
        return result;
    }

    public async Task<Settings> Update(Guid entityId, Dictionary<string, string> values, CancellationToken cancellationToken = default)
    {
        var settings = await settingsRepository.Update(entityId, values, cancellationToken) ??
            throw new AppException(ExceptionCodes.SettingsUnableToUpdate);

        await messagePublisher.Publish(new Message<Settings>(ActionNames.SettingsUpdated, settings), cancellationToken);

        return settings;
    }
}
