using FluentCMS.Entities.Sites;
using FluentCMS.Services;
using MediatR;

namespace FluentCMS.Application.Sites;
public class SiteHandlers(SiteService siteService) :
    IRequestHandler<GetSiteByIdQuery, Site>,
    IRequestHandler<GetSiteByUrlQuery, Site>,
    IRequestHandler<GetSitesQuery, IEnumerable<Site>>,
    IRequestHandler<CreateSiteCommand, Guid>,
    IRequestHandler<EditSiteCommand, Guid>,
    IRequestHandler<DeleteSiteCommand, Guid>,
    IRequestHandler<AddSiteUrlCommand>,
    IRequestHandler<RemoveSiteUrlCommand>
{
    public async Task<Site> Handle(GetSiteByIdQuery request, CancellationToken cancellationToken)
    {
        return await siteService.GetById(request.Id);
    }

    public async Task<Site> Handle(GetSiteByUrlQuery request, CancellationToken cancellationToken)
    {
        return await siteService.GetByUrl(request.Url);
    }

    public async Task<IEnumerable<Site>> Handle(GetSitesQuery request, CancellationToken cancellationToken)
    {
        return await siteService.GetAll();
    }

    public async Task<Guid> Handle(CreateSiteCommand request, CancellationToken cancellationToken)
    {
        var newId = Guid.NewGuid();
        var site = new Site(newId, request.Name, request.Description, request.URLs, request.RoleId);
        await siteService.Create(site);
        return newId;
    }

    public async Task<Guid> Handle(EditSiteCommand request, CancellationToken cancellationToken)
    {
        var site = await siteService.GetById(request.Id);
        if (site.Name != request.Name) site.SetName(request.Name);
        if (site.Description != request.Description) site.SetDescription(request.Description);
        if (site.RoleId != request.RoleId) site.SetRoleId(request.RoleId);
        site.Urls = request.URLs.ToList();
        await siteService.Update(site);
        return request.Id;
    }

    public async Task<Guid> Handle(DeleteSiteCommand request, CancellationToken cancellationToken)
    {
        await siteService.Delete(request.Id);
        return request.Id;
    }

    public async Task Handle(AddSiteUrlCommand request, CancellationToken cancellationToken)
    {
        var site = await siteService.GetById(request.SideId);
        site.AddUrl(request.NewUrl);
        await siteService.Update(site);
    }

    public async Task Handle(RemoveSiteUrlCommand request, CancellationToken cancellationToken)
    {
        await siteService.Delete(request.SiteId);
    }
}
