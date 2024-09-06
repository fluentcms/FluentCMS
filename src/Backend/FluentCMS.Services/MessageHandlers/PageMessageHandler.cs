using FluentCMS.Providers.MessageBusProviders;
using FluentCMS.Services.Models.Setup;

namespace FluentCMS.Services.MessageHandlers;

public class PageMessageHandler(IPageService pageService) : IMessageHandler<SiteTemplate>
{
    public async Task Handle(Message<SiteTemplate> notification, CancellationToken cancellationToken)
    {
        switch (notification.Action)
        {
            case ActionNames.SetupInitializePages:
                await CreatePageTemplates(null, notification.Payload.Pages, notification.Payload, cancellationToken);
                break;

            default:
                break;
        }
    }

    private async Task CreatePageTemplates(Guid? parentPageId, IEnumerable<PageTemplate> pageTemplates, SiteTemplate siteTemplate, CancellationToken cancellationToken = default)
    {
        var order = 0;
        foreach (var pageTemplate in pageTemplates)
        {
            await CreatePageTemplate(parentPageId, order, pageTemplate, siteTemplate, cancellationToken);
            order++;
        }
    }

    private async Task CreatePageTemplate(Guid? parentPageId, int order, PageTemplate pageTemplate, SiteTemplate siteTemplate, CancellationToken cancellationToken = default)
    {
        var layouts = siteTemplate.Layouts;
        var page = new Page
        {
            SiteId = siteTemplate.Id,
            ParentId = parentPageId,
            Path = pageTemplate.Path,
            Title = pageTemplate.Title,
            LayoutId = layouts.Where(x => x.Name == pageTemplate.Layout).SingleOrDefault()?.Id,
            DetailLayoutId = layouts.Where(x => x.Name == pageTemplate.DetailLayout).SingleOrDefault()?.Id,
            EditLayoutId = layouts.Where(x => x.Name == pageTemplate.EditLayout).SingleOrDefault()?.Id,
            Order = order,
            Locked = pageTemplate.Locked,
        };
        await pageService.Create(page, cancellationToken);
        pageTemplate.Id = page.Id;
        await CreatePageTemplates(page.Id, pageTemplate.Children, siteTemplate, cancellationToken);
    }
}
