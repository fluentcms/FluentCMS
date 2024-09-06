using FluentCMS.Providers.MessageBusProviders;
using FluentCMS.Services.Models.Setup;
using System.IO;

namespace FluentCMS.Services;

public interface ISetupService : IAutoRegisterService
{
    Task<IEnumerable<string>> GetTemplates(CancellationToken cancellationToken = default);
    Task<bool> Start(SetupTemplate setupTemplate, CancellationToken cancellationToken = default);
    Task<bool> IsInitialized(CancellationToken cancellationToken = default);
}

public class SetupService(IMessagePublisher messagePublisher, IGlobalSettingsRepository globalSettingsRepository) : ISetupService
{

    public const string SetupTemplatesFolder = "Templates";
    public const string SetupManifestFile = "manifest.json";

    public Task<IEnumerable<string>> GetTemplates(CancellationToken cancellationToken = default)
    {
        var templateFolders = Path.Combine(SetupTemplatesFolder);
        return Task.FromResult(Directory.GetDirectories(templateFolders).Select(x => new DirectoryInfo(x).Name));
    }

    public Task<bool> IsInitialized(CancellationToken cancellationToken = default)
    {
        return globalSettingsRepository.Any(cancellationToken);
    }

    public async Task<bool> Start(SetupTemplate setupTemplate, CancellationToken cancellationToken = default)
    {
        // loading layout data from files
        foreach (var layout in setupTemplate.Site.Layouts)
        {
            var bodyLayoutFilePath = Path.Combine(SetupTemplatesFolder, setupTemplate.Site.Template, $"{layout.Name}.body.html");
            var headLayoutFilePath = Path.Combine(SetupTemplatesFolder, setupTemplate.Site.Template, $"{layout.Name}.head.html");
            layout.Body = await System.IO.File.ReadAllTextAsync(bodyLayoutFilePath, cancellationToken);
            layout.Head = await System.IO.File.ReadAllTextAsync(headLayoutFilePath, cancellationToken);
        }

        SetIds(setupTemplate);

        await messagePublisher.Publish(new Message<SetupTemplate>(ActionNames.SetupStarted, setupTemplate), cancellationToken);
        await messagePublisher.Publish(new Message<SiteTemplate>(ActionNames.SetupInitializeSite, setupTemplate.Site), cancellationToken);
        await messagePublisher.Publish(new Message<SiteTemplate>(ActionNames.SetupInitializeLayouts, setupTemplate.Site), cancellationToken);
        await messagePublisher.Publish(new Message<SiteTemplate>(ActionNames.SetupInitializePages, setupTemplate.Site), cancellationToken);
        await messagePublisher.Publish(new Message<SiteTemplate>(ActionNames.SetupInitializePlugins, setupTemplate.Site), cancellationToken);

        return true;
    }

    private static void SetIds(SetupTemplate setupTemplate)
    {
        // set site, page, layout, etc. ids
        setupTemplate.Site.Id = Guid.NewGuid();
        foreach (var pluginDefinition in setupTemplate.PluginDefinitions)
        {
            pluginDefinition.Id = Guid.NewGuid();
        }

        foreach (var contentType in setupTemplate.ContentTypes)
        {
            contentType.Id = Guid.NewGuid();
        }

        foreach (var layout in setupTemplate.Site.Layouts)
        {
            layout.Id = Guid.NewGuid();
            layout.SiteId = setupTemplate.Site.Id;
        }

        foreach (var page in setupTemplate.Site.Pages)
        {
            SetIds(setupTemplate, page);
        }
    }

    private static void SetIds(SetupTemplate setupTemplate, PageTemplate page)
    {
        page.Id = Guid.NewGuid();
        page.SiteId = setupTemplate.Site.Id;
        foreach (var plugin in page.Plugins)
        {
            plugin.Id = Guid.NewGuid();
            plugin.DefinitionId = setupTemplate.PluginDefinitions.Where(p => p.Name.Equals(plugin.Definition, StringComparison.InvariantCultureIgnoreCase)).Single().Id;
        }
        foreach (var childPage in page.Children)
        {
            SetIds(setupTemplate, childPage);
        }
    }
}
