namespace FluentCMS.Services.MessageHandlers;

public class PageMessageHandler(IPageService pageService, IPermissionService permissionService) : IMessageHandler<SiteTemplate>, IMessageHandler<Layout>
{
    public async Task Handle(Message<Layout> notification, CancellationToken cancellationToken)
    {
        switch (notification.Action)
        {
            case ActionNames.LayoutDeleted:
                var siteId = notification.Payload.SiteId;
                var layoutId = notification.Payload.Id;
                var allPages = await pageService.GetBySiteId(siteId, cancellationToken);
                foreach (var page in allPages)
                {
                    if (page.LayoutId == layoutId)
                    {
                        page.LayoutId = null;
                        await pageService.Update(page, cancellationToken);
                    }
                    if (page.DetailLayoutId == layoutId)
                    {
                        page.DetailLayoutId = null;
                        await pageService.Update(page, cancellationToken);
                    }
                    if (page.EditLayoutId == layoutId)
                    {
                        page.EditLayoutId = null;
                        await pageService.Update(page, cancellationToken);
                    }
                }
                break;

            default:
                break;
        }
    }

    public async Task Handle(Message<SiteTemplate> notification, CancellationToken cancellationToken)
    {
        switch (notification.Action)
        {
            case ActionNames.SiteCreated:
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
        var roles = siteTemplate.Roles;
        var layouts = siteTemplate.Layouts;
        var page = new Page
        {
            Id = pageTemplate.Id,
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

        var adminRoles = roles.Where(r => pageTemplate.AdminRoles.Contains(r.Name)).Select(r => r.Id).ToList();
        var contributorRoles = roles.Where(r => pageTemplate.ContributorRoles.Contains(r.Name)).Select(r => r.Id).ToList();
        var viewRoles = roles.Where(r => pageTemplate.ViewRoles.Contains(r.Name)).Select(r => r.Id).ToList();
        await permissionService.Set(page.SiteId, page.Id, PagePermissionAction.PageAdmin, adminRoles, cancellationToken);
        await permissionService.Set(page.SiteId, page.Id, PagePermissionAction.PageView, viewRoles, cancellationToken);

        await CreatePageTemplates(page.Id, pageTemplate.Children, siteTemplate, cancellationToken);
    }

}
