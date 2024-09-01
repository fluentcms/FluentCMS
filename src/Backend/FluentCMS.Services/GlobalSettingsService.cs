namespace FluentCMS.Services;

public interface IGlobalSettingsService : IAutoRegisterService
{
    Task<GlobalSettings> Update(GlobalSettings settings, CancellationToken cancellationToken = default);
    Task<GlobalSettings?> Get(CancellationToken cancellationToken = default);
    Task<GlobalSettings> Init(GlobalSettings settings, CancellationToken cancellationToken = default);
    Task<bool> Reset(CancellationToken cancellationToken = default);
}

public class GlobalSettingsService(IGlobalSettingsRepository repository, IAuthContext authContext) : IGlobalSettingsService
{
    public async Task<GlobalSettings> Update(GlobalSettings settings, CancellationToken cancellationToken = default)
    {
        settings.SuperAdmins = settings.SuperAdmins.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();

        // at least one super admin should exist
        if (!settings.SuperAdmins.Any())
            throw new AppException(ExceptionCodes.GlobalSettingsSuperAdminAtLeastOne);

        var existSetting = await repository.Get(cancellationToken) ??
             throw new AppException(ExceptionCodes.GlobalSettingsNotFound);

        // if the current user is a super admin and is trying to remove himself from the super admin list, throw an exception
        if (existSetting.SuperAdmins.Contains(authContext.Username) && !settings.SuperAdmins.Contains(authContext.Username))
            throw new AppException(ExceptionCodes.GlobalSettingsSuperAdminCanNotBeDeleted);

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
