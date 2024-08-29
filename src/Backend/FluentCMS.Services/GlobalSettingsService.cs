namespace FluentCMS.Services;

public interface IGlobalSettingsService : IAutoRegisterService
{
    Task<GlobalSettings> Update(GlobalSettings settings, CancellationToken cancellationToken = default);
    Task<GlobalSettings?> Get(CancellationToken cancellationToken = default);
    Task<GlobalSettings> Init(GlobalSettings settings, CancellationToken cancellationToken = default);
    Task<bool> Reset(CancellationToken cancellationToken = default);
}

public class GlobalSettingsService(IGlobalSettingsRepository repository) : IGlobalSettingsService
{
    public async Task<GlobalSettings> Update(GlobalSettings settings, CancellationToken cancellationToken = default)
    {
        var existSetting = await repository.Get(cancellationToken) ??
             throw new AppException(ExceptionCodes.GlobalSettingsNotFound);

        existSetting.SuperAdmins = settings.SuperAdmins;

        return await repository.Update(existSetting, cancellationToken)
            ?? throw new AppException(ExceptionCodes.GlobalSettingsUnableToUpdate);
    }

    public async Task<GlobalSettings> Init(GlobalSettings settings, CancellationToken cancellationToken = default)
    {
        return await repository.Update(settings, cancellationToken)
             ?? throw new AppException(ExceptionCodes.GlobalSettingsUnableToUpdate);
    }

    public async Task<GlobalSettings?> Get(CancellationToken cancellationToken = default)
    {
        return await repository.Get(cancellationToken);
    }

    public async Task<bool> Reset(CancellationToken cancellationToken = default)
    {
        return await repository.Reset(cancellationToken);
    }

}
