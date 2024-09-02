namespace FluentCMS.Services.Setup.Handlers;

public class SiteHandler(ISiteService siteService) : BaseSetupHandler
{
    public override SetupSteps Step => SetupSteps.Site;

    public async override Task<SetupContext> Handle(SetupContext context)
    {
        Guid? layoutId = context.Layouts.Where(l => l.Name.Equals(context.AdminTemplate.Site.Layout?.ToLowerInvariant(), StringComparison.InvariantCultureIgnoreCase)).Select(l => l.Id).SingleOrDefault();
        Guid? editLayoutId = context.Layouts.Where(l => l.Name.Equals(context.AdminTemplate.Site.EditLayout?.ToLowerInvariant(), StringComparison.InvariantCultureIgnoreCase)).Select(l => l.Id).SingleOrDefault();
        Guid? detailLayoutId = context.Layouts.Where(l => l.Name.Equals(context.AdminTemplate.Site.DetailLayout?.ToLowerInvariant(), StringComparison.InvariantCultureIgnoreCase)).Select(l => l.Id).SingleOrDefault();
        if (layoutId == Guid.Empty || layoutId == null)
            layoutId = context.DefaultLayoutId;

        if (editLayoutId == Guid.Empty || editLayoutId == null)
            editLayoutId = context.DefaultLayoutId;

        if (detailLayoutId == Guid.Empty || detailLayoutId == null)
            detailLayoutId = context.DefaultLayoutId;

        var site = new Site
        {
            Name = context.AdminTemplate.Site.Name,
            Description = context.AdminTemplate.Site.Description,
            Urls = [context.SetupRequest.AdminDomain],
            LayoutId = layoutId.Value,
            EditLayoutId = editLayoutId.Value,
            DetailLayoutId = detailLayoutId.Value,
        };

        context.Site = await siteService.Create(site);

        return await base.Handle(context);
    }
}
