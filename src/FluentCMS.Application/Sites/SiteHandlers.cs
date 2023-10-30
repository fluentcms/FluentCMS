using FluentCMS.Entities.Sites;
using FluentCMS.Services;
using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Application.Sites;
public class SiteHandlers
    :
    IRequestHandler<GetSiteByUrl, Site>,
    IRequestHandler<GetSiteByIdQuery, Site>,
    IRequestHandler<GetSitesQuery, IEnumerable<Site>>,
    IRequestHandler<CreateSiteCommand, Guid>,
    IRequestHandler<EditSiteCommand, Guid>,
    IRequestHandler<DeleteSiteCommand, Guid>,
    IRequestHandler<AddUrlSiteCommand>,
    IRequestHandler<RemoveUrlSiteCommand>
{
    private readonly SiteService _siteService;

    public SiteHandlers(SiteService siteService)
    {
        _siteService = siteService;
    }
    async Task IRequestHandler<RemoveUrlSiteCommand>.Handle(RemoveUrlSiteCommand request, CancellationToken cancellationToken)
    {
        await _siteService.Delete(request.Id);
    }

    async Task IRequestHandler<AddUrlSiteCommand>.Handle(AddUrlSiteCommand request, CancellationToken cancellationToken)
    {
        var site = await _siteService.GetById(request.Id);
        site.AddUrl(request.NewUrl);
        await _siteService.Update(site);
    }

    async Task<Guid> IRequestHandler<DeleteSiteCommand, Guid>.Handle(DeleteSiteCommand request, CancellationToken cancellationToken)
    {
        await _siteService.Delete(request.Id);
        return request.Id;
    }

    async Task<Guid> IRequestHandler<EditSiteCommand, Guid>.Handle(EditSiteCommand request, CancellationToken cancellationToken)
    {
        var site = await _siteService.GetById(request.Id);
        if (site.Name != request.Name) site.SetName(request.Name);
        if (site.Description != request.Description) site.SetDescription(request.Description);
        await _siteService.Update(site);
        return request.Id;
    }

    async Task<Guid> IRequestHandler<CreateSiteCommand, Guid>.Handle(CreateSiteCommand request, CancellationToken cancellationToken)
    {
        var newId = Guid.NewGuid();
        var site = new Site(newId, request.Name, request.Description, request.URLs, request.RoleId);
        await _siteService.Create(site);
        return newId;
    }

    async Task<IEnumerable<Site>> IRequestHandler<GetSitesQuery, IEnumerable<Site>>.Handle(GetSitesQuery request, CancellationToken cancellationToken)
    {
        return await _siteService.GetAll();
    }

    async Task<Site> IRequestHandler<GetSiteByIdQuery, Site>.Handle(GetSiteByIdQuery request, CancellationToken cancellationToken)
    {
        return await _siteService.GetById(request.Id);
    }

    async Task<Site> IRequestHandler<GetSiteByUrl, Site>.Handle(GetSiteByUrl request, CancellationToken cancellationToken)
    {
        return await _siteService.GetByUrl(request.Url);

    }
}
