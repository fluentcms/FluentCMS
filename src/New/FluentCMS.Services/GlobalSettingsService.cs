namespace FluentCMS.Services;

public interface IGlobalSettingsService : IService
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
        // Host should have at least one super user
        CheckSuperUsers(settings);

        // super admin can't remove himself from super user list
        if (!settings.SuperUsers.Contains(authContext.Username))
            throw new AppException(ExceptionCodes.GlobalSettingsUnableToRemoveYourself);

        return await repository.Update(settings, cancellationToken)
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

    private static void CheckSuperUsers(GlobalSettings systemSettings)
    {
        // host should have at least one super user
        if (systemSettings.SuperUsers.Count == 0)
            throw new AppException(ExceptionCodes.GlobalSettingsAtLeastOneSuperUser);
    }
}
