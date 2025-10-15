namespace FluentCMS.Api.Core.Services;

public interface IGlobalSettingsService
{
    Task<GlobalSettings> Update(GlobalSettings settings, CancellationToken cancellationToken = default);
    Task<GlobalSettings> Get(CancellationToken cancellationToken = default);
}

public class GlobalSettingsService(IGlobalSettingsRepository repository, IApplicationExecutionContext executionContext, IEventPublisher  eventPublisher) : IGlobalSettingsService
{
    public async Task<GlobalSettings> Update(GlobalSettings settings, CancellationToken cancellationToken = default)
    {
        settings.SuperAdmins = [.. settings.SuperAdmins.Where(x => !string.IsNullOrEmpty(x)).Distinct()];

        // at least one super admin should exist
        if (settings.SuperAdmins.Count == 0)
            throw new EnhancedException(ExceptionCodes.GlobalSettingsSuperAdminAtLeastOne);

        var existSetting = await repository.Get(cancellationToken) ?? new();

        // if the current user is a super admin and is trying to remove himself from the super admin list, throw an exception
        if (existSetting.SuperAdmins.Contains(executionContext.Username) && !settings.SuperAdmins.Contains(executionContext.Username))
            throw new EnhancedException(ExceptionCodes.GlobalSettingsSuperAdminCanNotBeDeleted);

        existSetting.SuperAdmins = settings.SuperAdmins;

        var updated = await repository.Update(existSetting, cancellationToken)
            ?? throw new EnhancedException(ExceptionCodes.GlobalSettingsUnableToUpdate);

        await eventPublisher.Publish(new GlobalSettingsUpdatedEvent(updated), cancellationToken);

        return updated;
    }

    public async Task<GlobalSettings> Get(CancellationToken cancellationToken = default)
    {
        return await repository.Get(cancellationToken) ??
                throw new EnhancedException(ExceptionCodes.GlobalSettingsNotFound);
    }
}
