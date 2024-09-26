namespace FluentCMS.Services.MessageHandlers;

public class PluginMessageHandler(IPluginService pluginService, IPluginContentService pluginContentService) : IMessageHandler<SiteTemplate>
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
                Settings = pluginTemplate.Settings
            };

            await pluginService.Create(plugin, cancellationToken);

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
