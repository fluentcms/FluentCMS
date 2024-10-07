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
        return await settingsRepository.GetById(entityId, cancellationToken) ??
            throw new AppException(ExceptionCodes.SettingsNotFound);
    }

    public async Task<IEnumerable<Settings>> GetByIds(IEnumerable<Guid> entityIds, CancellationToken cancellationToken = default)
    {
        return await settingsRepository.GetByIds(entityIds, cancellationToken);
    }

    public async Task<Settings> Update(Guid entityId, Dictionary<string, string> values, CancellationToken cancellationToken = default)
    {
        var settings = await settingsRepository.Update(entityId, values, cancellationToken) ??
            throw new AppException(ExceptionCodes.SettingsUnableToUpdate);

        await messagePublisher.Publish(new Message<Settings>(ActionNames.SettingsUpdated, settings), cancellationToken);

        return settings;
    }
}
