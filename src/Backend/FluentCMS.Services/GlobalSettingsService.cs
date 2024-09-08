using FluentCMS.Providers.MessageBusProviders;

namespace FluentCMS.Services;

public interface IGlobalSettingsService : IAutoRegisterService
{
    Task<GlobalSettings> Update(GlobalSettings settings, CancellationToken cancellationToken = default);
    Task<GlobalSettings> Get(CancellationToken cancellationToken = default);
    Task<bool> SetInitialized(CancellationToken cancellationToken = default);
}

public class GlobalSettingsService(IGlobalSettingsRepository repository, IApiExecutionContext apiExecutionContext, IMessagePublisher messagePublisher) : IGlobalSettingsService
{
    public async Task<GlobalSettings> Update(GlobalSettings settings, CancellationToken cancellationToken = default)
    {
        settings.SuperAdmins = settings.SuperAdmins.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();

        // at least one super admin should exist
        if (!settings.SuperAdmins.Any())
            throw new AppException(ExceptionCodes.GlobalSettingsSuperAdminAtLeastOne);

        var existSetting = await repository.Get(cancellationToken) ?? new();

        // if the current user is a super admin and is trying to remove himself from the super admin list, throw an exception
        if (existSetting.SuperAdmins.Contains(apiExecutionContext.Username) && !settings.SuperAdmins.Contains(apiExecutionContext.Username))
            throw new AppException(ExceptionCodes.GlobalSettingsSuperAdminCanNotBeDeleted);

        existSetting.SuperAdmins = settings.SuperAdmins;

        var updated = await repository.Update(existSetting, cancellationToken)
            ?? throw new AppException(ExceptionCodes.GlobalSettingsUnableToUpdate);

        await messagePublisher.Publish(new Message<GlobalSettings>(ActionNames.GlobalSettingsUpdated, updated), cancellationToken);

        return updated;
    }

    public async Task<GlobalSettings> Get(CancellationToken cancellationToken = default)
    {
        return await repository.Get(cancellationToken) ??
                throw new AppException(ExceptionCodes.GlobalSettingsNotFound);
    }

    public async Task<bool> SetInitialized(CancellationToken cancellationToken = default)
    {
        var globalSettings = await repository.Get(cancellationToken) ?? new();
        globalSettings.Initialized = true;
        await repository.Update(globalSettings, cancellationToken);
        return true;
    }
}
