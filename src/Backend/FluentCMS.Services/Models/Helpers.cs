using System.IO;
using System.Text.Json;

namespace FluentCMS.Services.Models;

public static class Helpers
{
    public static async Task Load(this SetupTemplate setupTemplate, CancellationToken cancellationToken = default)
    {
        var manifestFilePath = Path.Combine(ServiceConstants.SetupTemplatesFolder, setupTemplate.Template, ServiceConstants.SetupManifestFile);

        if (!System.IO.File.Exists(manifestFilePath))
            throw new AppException($"{ServiceConstants.SetupManifestFile} doesn't exist!");

        var jsonSerializerOptions = new JsonSerializerOptions();
        jsonSerializerOptions.Converters.Add(new DictionaryJsonConverter());

        var jsonSetupTemplate = await JsonSerializer.DeserializeAsync<SetupTemplate>(System.IO.File.OpenRead(manifestFilePath), jsonSerializerOptions, cancellationToken) ??
               throw new AppException($"Failed to read/deserialize {ServiceConstants.SetupManifestFile}");

        setupTemplate.PluginDefinitions = jsonSetupTemplate.PluginDefinitions;
        setupTemplate.ContentTypes = jsonSetupTemplate.ContentTypes;
        setupTemplate.Site = new SiteTemplate
        {
            Url = setupTemplate.Url,
            Template = setupTemplate.Template
        };
        SetIds(setupTemplate);
        await setupTemplate.Site.Load(cancellationToken);
        SetIds(setupTemplate.Site, setupTemplate.PluginDefinitions);
    }

    public static async Task Load(this SiteTemplate siteTemplate, CancellationToken cancellationToken = default)
    {
        var siteFilePath = Path.Combine(ServiceConstants.SetupTemplatesFolder, siteTemplate.Template, ServiceConstants.SetupSiteTemplateFile);

        if (!System.IO.File.Exists(siteFilePath))
            throw new AppException($"{ServiceConstants.SetupSiteTemplateFile} doesn't exist!");

        var jsonSerializerOptions = new JsonSerializerOptions();
        jsonSerializerOptions.Converters.Add(new DictionaryJsonConverter());

        var jsonSiteTemplate = await JsonSerializer.DeserializeAsync<SiteTemplate>(System.IO.File.OpenRead(siteFilePath), jsonSerializerOptions, cancellationToken) ??
               throw new AppException($"Failed to read/deserialize {ServiceConstants.SetupSiteTemplateFile}");

        siteTemplate.Name = jsonSiteTemplate.Name;
        siteTemplate.Description = jsonSiteTemplate.Description;
        siteTemplate.Layout = jsonSiteTemplate.Layout;
        siteTemplate.EditLayout = jsonSiteTemplate.EditLayout;
        siteTemplate.DetailLayout = jsonSiteTemplate.DetailLayout;
        siteTemplate.Layouts = jsonSiteTemplate.Layouts;
        siteTemplate.Pages = jsonSiteTemplate.Pages;
        siteTemplate.Roles = jsonSiteTemplate.Roles;
        siteTemplate.AdminRoles = jsonSiteTemplate.AdminRoles;
        siteTemplate.ContributorRoles = jsonSiteTemplate.ContributorRoles;

        // loading layout data from files
        foreach (var layout in siteTemplate.Layouts)
        {
            var bodyLayoutFilePath = Path.Combine(ServiceConstants.SetupTemplatesFolder, siteTemplate.Template, $"{layout.Name}.body.html");
            var headLayoutFilePath = Path.Combine(ServiceConstants.SetupTemplatesFolder, siteTemplate.Template, $"{layout.Name}.head.html");
            layout.Body = await System.IO.File.ReadAllTextAsync(bodyLayoutFilePath, cancellationToken);
            layout.Head = await System.IO.File.ReadAllTextAsync(headLayoutFilePath, cancellationToken);
        }
    }

    private static void SetIds(SetupTemplate setupTemplate)
    {
        foreach (var pluginDefinition in setupTemplate.PluginDefinitions)
        {
            pluginDefinition.Id = Guid.NewGuid();
        }

        foreach (var contentType in setupTemplate.ContentTypes)
        {
            contentType.Id = Guid.NewGuid();
        }
    }

    private static void SetIds(SiteTemplate siteTemplate, IEnumerable<PluginDefinition> pluginDefinitions)
    {
        // set site, page, layout, etc. ids
        siteTemplate.Id = Guid.NewGuid();

        foreach (var layout in siteTemplate.Layouts)
        {
            layout.Id = Guid.NewGuid();
            layout.SiteId = siteTemplate.Id;
        }

        foreach (var role in siteTemplate.Roles)
        {
            role.Id = Guid.NewGuid();
            role.SiteId = siteTemplate.Id;
        }

        foreach (var page in siteTemplate.Pages)
        {
            SetIds(siteTemplate, page, pluginDefinitions);
        }
    }

    private static void SetIds(SiteTemplate siteTemplate, PageTemplate page, IEnumerable<PluginDefinition> pluginDefinitions)
    {
        page.Id = Guid.NewGuid();
        page.SiteId = siteTemplate.Id;
        foreach (var plugin in page.Plugins)
        {
            plugin.Id = Guid.NewGuid();
            plugin.DefinitionId = pluginDefinitions.Where(p => p.Name.Equals(plugin.Definition, StringComparison.InvariantCultureIgnoreCase)).Single().Id;
        }
        foreach (var childPage in page.Children)
        {
            SetIds(siteTemplate, childPage, pluginDefinitions);
        }
    }
}
