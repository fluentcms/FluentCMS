namespace FluentCMS.Services.MessageHandlers;

public class PluginMessageHandler(IPluginService pluginService, IPluginContentService pluginContentService, ISettingsService settingsService) : IMessageHandler<SiteTemplate>
{
    public async Task Handle(Message<SiteTemplate> notification, CancellationToken cancellationToken)
    {
        switch (notification.Action)
        {
            case ActionNames.SetupInitializePlugins:
                await CreatePlugins(notification.Payload, cancellationToken);
                break;

            default:
                break;
        }
    }

    private async Task CreatePlugins(SiteTemplate siteTemplate, CancellationToken cancellationToken)
    {
        foreach (var page in siteTemplate.Pages)
        {
            await CreatePlugins(page, cancellationToken);
        }
    }

    private async Task CreatePlugins(PageTemplate pageTemplate, CancellationToken cancellationToken)
    {
        var order = 0;
        foreach (var pluginTemplate in pageTemplate.Plugins)
        {
            var plugin = new Plugin
            {
                Id = pluginTemplate.Id,
                Order = order++,
                Section = pluginTemplate.Section,
                DefinitionId = pluginTemplate.DefinitionId,
                PageId = pageTemplate.Id,
                SiteId = pageTemplate.SiteId,
                Locked = pluginTemplate.Locked,
                Cols = pluginTemplate.Cols,
                ColsMd = pluginTemplate.ColsMd,
                ColsLg = pluginTemplate.ColsLg,
            };

            await pluginService.Create(plugin, cancellationToken);

            var pluginContentBasePath = System.IO.Path.Combine(ServiceConstants.SetupTemplatesFolder, pageTemplate.Template, ServiceConstants.SetupPagesFolder);

            if (pluginTemplate.Settings != null && pluginTemplate.Settings.Count != 0) 
            {
                if(pluginTemplate.Settings.TryGetValue("Template", out var template))
                {
                    if (template.EndsWith(".sbn", StringComparison.OrdinalIgnoreCase))
                    {
                        var filePath = System.IO.Path.Combine(pluginContentBasePath, template);
                        if (System.IO.File.Exists(filePath))
                        {
                            var fileContent = await System.IO.File.ReadAllTextAsync(filePath, cancellationToken);
                            pluginTemplate.Settings["Template"] = fileContent;
                        }
                    }
                }
                await settingsService.Update(plugin.Id, pluginTemplate.Settings, cancellationToken);
            }

            if (!string.IsNullOrEmpty(pluginTemplate.ContentPath))
            {
                var filePath = System.IO.Path.Combine(pluginContentBasePath, pluginTemplate.ContentPath);

                if (System.IO.File.Exists(filePath))
                {
                    var fileContent = await System.IO.File.ReadAllTextAsync(filePath, cancellationToken);
                    
                    var pluginContent = new PluginContent
                    {
                        SiteId = pageTemplate.SiteId,
                        PluginId = plugin.Id,
                        Type = "TextHTMLContent",
                        Data = new Dictionary<string, object?> {
                            { "Content", fileContent }
                        }
                    };

                    await pluginContentService.Create(pluginContent, cancellationToken);
                }
            }
            
            if (pluginTemplate.Content != null)
            {
                foreach (var pluginContentTemplate in pluginTemplate.Content)
                {
                    var pluginContent = new PluginContent
                    {
                        SiteId = pageTemplate.SiteId,
                        PluginId = plugin.Id,
                        Type = pluginTemplate.Type,
                        Data = pluginContentTemplate ?? []
                    };

                    await pluginContentService.Create(pluginContent, cancellationToken);
                }
            }
        }

        foreach (var childPage in pageTemplate.Children)
        {
            await CreatePlugins(childPage, cancellationToken);
        }
    }
}
