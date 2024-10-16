using System.IO;
using System.Text.Json;

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

        var manifestFilePath = Path.Combine(ServiceConstants.SetupTemplatesFolder, setupTemplate.Template, ServiceConstants.SetupManifestFile);

        if (!System.IO.File.Exists(manifestFilePath))
            throw new AppException($"{ServiceConstants.SetupManifestFile} doesn't exist!");

        var jsonSerializerOptions = new JsonSerializerOptions();
        jsonSerializerOptions.Converters.Add(new DictionaryJsonConverter());

        var jsonSetupTemplate = await JsonSerializer.DeserializeAsync<SetupTemplate>(System.IO.File.OpenRead(manifestFilePath), jsonSerializerOptions) ??
               throw new AppException($"Failed to read/deserialize {ServiceConstants.SetupManifestFile}");

        setupTemplate.PluginDefinitions = jsonSetupTemplate.PluginDefinitions;
        setupTemplate.ContentTypes = jsonSetupTemplate.ContentTypes;

        setupTemplate.Site = new SiteTemplate
        {
            Url = setupTemplate.Url,
            Template = setupTemplate.Template
        };

        // since we need ids for the child object relations,
        // we should set ids for all entities manually
        SetIds(setupTemplate);

        await messagePublisher.Publish(new Message<SetupTemplate>(ActionNames.SetupStarted, setupTemplate), cancellationToken);
        await messagePublisher.Publish(new Message<SiteTemplate>(ActionNames.SetupInitializeSite, setupTemplate.Site), cancellationToken);
        await messagePublisher.Publish(new Message<SetupTemplate>(ActionNames.SetupCompleted, setupTemplate), cancellationToken);

        return true;
    }

    private static void SetIds(SetupTemplate setupTemplate)
    {
        foreach (var pluginDefinition in setupTemplate.PluginDefinitions)
            pluginDefinition.Id = Guid.NewGuid();

        foreach (var contentType in setupTemplate.ContentTypes)
            contentType.Id = Guid.NewGuid();
    }
}
