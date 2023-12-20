using FluentCMS.Entities;
using FluentCMS.Repositories;

namespace FluentCMS.Services;

public interface ISystemSettingsService
{
    Task<SystemSettings> Create(SystemSettings systemSettings, CancellationToken cancellationToken = default);
    Task<SystemSettings> Update(SystemSettings systemSettings, CancellationToken cancellationToken = default);
    Task<SystemSettings> Get(CancellationToken cancellationToken = default);
    Task<bool> IsInitialized(CancellationToken cancellationToken = default);
}

public class SystemSettingsService(
    ISystemSettingsRepository systemSettingsRepository,
    IApplicationContext appContext) : ISystemSettingsService
{

    public async Task<SystemSettings> Create(SystemSettings systemSettings, CancellationToken cancellationToken = default)
    {
        // There is no need to check for super admin here
        // Because this method will be called only once on installation
        // Host should have at least one super user
        CheckSuperUsers(systemSettings);

        // Checking if host record exists or not. if exists, throw exception
        // this should be called only for the first time on installation
        if (await IsInitialized(cancellationToken))
            throw new AppException(ExceptionCodes.SystemSettingsAlreadyInitialized);

        return await systemSettingsRepository.Create(systemSettings, cancellationToken) ??
            throw new AppException(ExceptionCodes.SystemSettingsUnableToCreate);
    }

    public async Task<SystemSettings> Update(SystemSettings systemSettings, CancellationToken cancellationToken = default)
    {
        // Host should have at least one super user
        CheckSuperUsers(systemSettings);

        _ = await systemSettingsRepository.Get(cancellationToken)
            ?? throw new AppException(ExceptionCodes.SystemSettingsNotFound);

        // super admin can't remove himself from super user list
        if (!systemSettings.SuperUsers.Contains(appContext.Username))
            throw new AppException(ExceptionCodes.SystemSettingsUnableToRemoveYourself);

        return await systemSettingsRepository.Update(systemSettings, cancellationToken)
            ?? throw new AppException(ExceptionCodes.SystemSettingsUnableToUpdate);
    }

    public async Task<SystemSettings> Get(CancellationToken cancellationToken = default)
    {
        return await systemSettingsRepository.Get(cancellationToken)
            ?? throw new AppException(ExceptionCodes.SystemSettingsNotFound);
    }

    private static void CheckSuperUsers(SystemSettings systemSettings)
    {
        // host should have at least one super user
        if (systemSettings.SuperUsers.Count == 0)
            throw new AppException(ExceptionCodes.SystemSettingsAtLeastOneSuperUser);
    }

    public async Task<bool> IsInitialized(CancellationToken cancellationToken = default)
    {
        var settings = await systemSettingsRepository.Get(cancellationToken);
        return settings != null;
    }
}
