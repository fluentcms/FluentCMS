using FluentCMS.Services.Setup.Models;

namespace FluentCMS.Services.Setup.Handlers;

public class PageHandler(IPageService pageService, IPluginService pluginService, IPluginContentService pluginContentService) : BaseSetupHandler
{
    public override SetupSteps Step => SetupSteps.Page;

    public async override Task<SetupContext> Handle(SetupContext context)
    {
        var order = 0;
        foreach (var pageTemplate in context.AdminTemplate.Pages)
        {
            var _page = await pageService.Create(GetPage(context, pageTemplate, null, order));
            order++;
            await InitPagePlugins(context, pageTemplate, _page.Id);
            context.Pages.Add(_page);
            var childOrder = 0;
            foreach (var child in pageTemplate.Children)
            {
                var childPage = await pageService.Create(GetPage(context, child, _page.Id, childOrder));
                context.Pages.Add(childPage);
                await InitPagePlugins(context, child, childPage.Id);
                childOrder++;
            }
        }

        return await base.Handle(context);
    }

    private async Task InitPagePlugins(SetupContext context, PageTemplate pageTemplate, Guid pageId)
    {
        var order = 0;
        foreach (var pluginTemplate in pageTemplate.Plugins)
        {
            var pluginDefinition = context.PluginDefinitions.Where(p => p.Name.Equals(pluginTemplate.Definition, StringComparison.InvariantCultureIgnoreCase)).Single();
            var plugin = new Plugin
            {
                Order = order,
                Section = pluginTemplate.Section,
                DefinitionId = pluginDefinition.Id,
                PageId = pageId,
                SiteId = context.Site.Id,
                Locked = pluginTemplate.Locked,
                Cols = pluginTemplate.Cols,
                ColsMd = pluginTemplate.ColsMd,
                ColsLg = pluginTemplate.ColsLg,
                Settings = pluginTemplate.Settings
            };
            order++;
            var pluginResponse = await pluginService.Create(plugin);
            if (pluginTemplate.Content != null)
            {
                foreach (var pluginContentTemplate in pluginTemplate.Content)
                {
                    var pluginContent = new PluginContent
                    {
                        PluginId = pluginResponse.Id,
                        Type = pluginTemplate.Type,
                        Data = pluginContentTemplate
                    };

                    await pluginContentService.Create(pluginContent);
                }
            }
        }
    }

    private Page GetPage(SetupContext context, PageTemplate pageTemplate, Guid? parentId, int order)
    {
        Guid? layoutId = context.Layouts.Where(l => l.Name.Equals(pageTemplate?.Layout?.ToLowerInvariant(), StringComparison.InvariantCultureIgnoreCase)).Select(l => l.Id).SingleOrDefault();
        Guid? editLayoutId = context.Layouts.Where(l => l.Name.Equals(pageTemplate?.EditLayout?.ToLowerInvariant(), StringComparison.InvariantCultureIgnoreCase)).Select(l => l.Id).SingleOrDefault();
        Guid? detailLayoutId = context.Layouts.Where(l => l.Name.Equals(pageTemplate?.DetailLayout?.ToLowerInvariant(), StringComparison.InvariantCultureIgnoreCase)).Select(l => l.Id).SingleOrDefault();

        if (layoutId == Guid.Empty)
            layoutId = null;

        if (editLayoutId == Guid.Empty)
            editLayoutId = null;

        if (detailLayoutId == Guid.Empty)
            detailLayoutId = null;

        var page = new Page
        {
            Title = pageTemplate.Title,
            Path = pageTemplate.Path,
            LayoutId = layoutId,
            EditLayoutId = editLayoutId,
            DetailLayoutId = detailLayoutId,
            SiteId = context.Site.Id,
            ParentId = parentId,
            Order = order,
            Locked = pageTemplate.Locked
        };

        return page;
    }

}
