using System.IO;

namespace FluentCMS.Services;

public interface ISetupService : IAutoRegisterService
{
    Task<IEnumerable<string>> GetTemplates(CancellationToken cancellationToken = default);
    Task<bool> Start(SetupTemplate setupTemplate, CancellationToken cancellationToken = default);
    Task<bool> IsInitialized(CancellationToken cancellationToken = default);
}

public class SetupService(IMessagePublisher messagePublisher, IGlobalSettingsRepository globalSettingsRepository, IPermissionManager permissionManager) : ISetupService
{
    public Task<IEnumerable<string>> GetTemplates(CancellationToken cancellationToken = default)
    {
        var templateFolders = Path.Combine(ServiceConstants.SetupTemplatesFolder);
        return Task.FromResult(Directory.GetDirectories(templateFolders).Select(x => new DirectoryInfo(x).Name));
    }

    public Task<bool> IsInitialized(CancellationToken cancellationToken = default)
    {
        return globalSettingsRepository.Initialized(cancellationToken);
    }

    public async Task<bool> Start(SetupTemplate setupTemplate, CancellationToken cancellationToken = default)
    {
        if (await globalSettingsRepository.Initialized(cancellationToken))
            throw new AppException(ExceptionCodes.SetupAlreadyInitialized);

        if (!await permissionManager.HasAccess(GlobalPermissionAction.SuperAdmin, cancellationToken))
            throw new AppException(ExceptionCodes.PermissionDenied);

        await messagePublisher.Publish(new Message<SetupTemplate>(ActionNames.SetupStarted, setupTemplate), cancellationToken);
        await messagePublisher.Publish(new Message<SiteTemplate>(ActionNames.SetupInitializeSite, setupTemplate.Site), cancellationToken);
        await messagePublisher.Publish(new Message<SetupTemplate>(ActionNames.SetupCompleted, setupTemplate), cancellationToken);

        return true;
    }
}
