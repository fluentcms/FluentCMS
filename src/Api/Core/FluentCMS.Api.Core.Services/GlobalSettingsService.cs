namespace FluentCMS.Api.Core.Services;

public interface IGlobalSettingsService
{
    Task<GlobalSettings> Update(GlobalSettings settings, CancellationToken cancellationToken = default);
    Task<GlobalSettings> Get(CancellationToken cancellationToken = default);
}

internal class GlobalSettingsService(IGlobalSettingsRepository repository, ISecurityContext securityContext, IEventPublisher eventPublisher) : IGlobalSettingsService
{
    public async Task<GlobalSettings> Update(GlobalSettings settings, CancellationToken cancellationToken = default)
    {
        settings.SuperAdmins = [.. settings.SuperAdmins.Where(x => !string.IsNullOrEmpty(x)).Distinct()];

        // at least one super admin should exist
        if (settings.SuperAdmins.Count == 0)
            throw new EnhancedException(ExceptionCodes.GlobalSettingsSuperAdminAtLeastOne);

        var existSetting = await repository.Get(cancellationToken) ?? new();

        // if the current user is a super admin and is trying to remove himself from the super admin list, throw an exception
        if (existSetting.SuperAdmins.Contains(securityContext.Username) && !settings.SuperAdmins.Contains(securityContext.Username))
            throw new EnhancedException(ExceptionCodes.GlobalSettingsSuperAdminCanNotBeDeleted);

        existSetting.SuperAdmins = settings.SuperAdmins;

        await repository.Update(existSetting, cancellationToken);

        await eventPublisher.Publish(new GlobalSettingsUpdatedEvent(existSetting), cancellationToken);

        return existSetting;
    }

    public async Task<GlobalSettings> Get(CancellationToken cancellationToken = default)
    {
        return await repository.Get(cancellationToken) ??
                throw new EnhancedException(ExceptionCodes.GlobalSettingsNotFound);
    }
}
