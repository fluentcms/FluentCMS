using FluentCMS.Entities.Sites;
using FluentCMS.Services;
using MediatR;

namespace FluentCMS.Application.Sites;
public class SiteHandlers(SiteService siteService)
    :
    IRequestHandler<GetSiteByUrl, Site>,
    IRequestHandler<GetSiteByIdQuery, Site>,
    IRequestHandler<GetSitesQuery, IEnumerable<Site>>,
    IRequestHandler<CreateSiteCommand, Guid>,
    IRequestHandler<EditSiteCommand, Guid>,
    IRequestHandler<DeleteSiteCommand, Guid>,
    IRequestHandler<AddSiteUrlCommand>,
    IRequestHandler<RemoveSiteUrlCommand>
{
    async Task IRequestHandler<RemoveSiteUrlCommand>.Handle(RemoveSiteUrlCommand request, CancellationToken cancellationToken)
    {
        await siteService.Delete(request.SiteId);
    }

    async Task IRequestHandler<AddSiteUrlCommand>.Handle(AddSiteUrlCommand request, CancellationToken cancellationToken)
    {
        var site = await siteService.GetById(request.SideId);
        site.AddUrl(request.NewUrl);
        await siteService.Update(site);
    }

    public async Task<Guid> Handle(DeleteSiteCommand request, CancellationToken cancellationToken)
    {
        await siteService.Delete(request.SiteId);
        return request.SiteId;
    }

    public async Task<Guid> Handle(EditSiteCommand request, CancellationToken cancellationToken)
    {
        var site = await siteService.GetById(request.SiteId);
        if (site.Name != request.Name) site.SetName(request.Name);
        if (site.Description != request.Description) site.SetDescription(request.Description);
        await siteService.Update(site);
        return request.SiteId;
    }

    public async Task<Guid> Handle(CreateSiteCommand request, CancellationToken cancellationToken)
    {
        var newId = Guid.NewGuid();
        var site = new Site(newId, request.Name, request.Description, request.URLs, request.RoleId);
        await siteService.Create(site);
        return newId;
    }

    public async Task<IEnumerable<Site>> Handle(GetSitesQuery request, CancellationToken cancellationToken)
    {
        return await siteService.GetAll();
    }

    public async Task<Site> Handle(GetSiteByIdQuery request, CancellationToken cancellationToken)
    {
        return await siteService.GetById(request.SiteId);
    }

    public async Task<Site> Handle(GetSiteByUrl request, CancellationToken cancellationToken)
    {
        return await siteService.GetByUrl(request.Url);

    }
}
