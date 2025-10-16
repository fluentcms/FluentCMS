namespace FluentCMS.Api.Core.Services;

public interface ISetupService
{
    Task<IEnumerable<string>> GetTemplates(CancellationToken cancellationToken = default);
    Task<bool> Start(SetupTemplate setupTemplate, CancellationToken cancellationToken = default);
    Task<bool> IsInitialized(CancellationToken cancellationToken = default);
}

internal class SetupService(IEventPublisher eventPublisher, ISetupRepository setupRepository) : ISetupService
{
    public Task<IEnumerable<string>> GetTemplates(CancellationToken cancellationToken = default)
    {
        var templateFolders = Path.Combine(ServiceConstants.SetupTemplatesFolder);
        return Task.FromResult(Directory.GetDirectories(templateFolders).Select(x => new DirectoryInfo(x).Name));
    }

    public Task<bool> IsInitialized(CancellationToken cancellationToken = default)
    {
        return setupRepository.IsInitialized(cancellationToken);
    }

    public async Task<bool> Start(SetupTemplate setupTemplate, CancellationToken cancellationToken = default)
    {
        if (await setupRepository.IsInitialized(cancellationToken))
            throw new EnhancedException(ExceptionCodes.SetupAlreadyInitialized);

        //var manifestFilePath = Path.Combine(ServiceConstants.SetupTemplatesFolder, setupTemplate.Template, ServiceConstants.SetupManifestFile);

        //if (!System.IO.File.Exists(manifestFilePath))
        //    throw new EnhancedException($"{ServiceConstants.SetupManifestFile} doesn't exist!");

        //var jsonSerializerOptions = new JsonSerializerOptions();
        //jsonSerializerOptions.Converters.Add(new DictionaryJsonConverter());

        //var jsonSetupTemplate = await JsonSerializer.DeserializeAsync<SetupTemplate>(System.IO.File.OpenRead(manifestFilePath), jsonSerializerOptions) ??
        //       throw new EnhancedException($"Failed to read/deserialize {ServiceConstants.SetupManifestFile}");

        //setupTemplate.Site = new SiteTemplate
        //{
        //    Url = setupTemplate.Url,
        //    Template = setupTemplate.Template
        //};

        //// since we need ids for the child object relations,
        //// we should set ids for all entities manually
        //SetIds(setupTemplate);

        //await eventPublisher.Publish(new SetupStartedEvent(setupTemplate), cancellationToken);

        return true;
    }

    //private static void SetIds(SetupTemplate setupTemplate)
    //{
    //    foreach (var pluginDefinition in setupTemplate.PluginDefinitions)
    //        pluginDefinition.Id = Guid.NewGuid();
    //}
}
