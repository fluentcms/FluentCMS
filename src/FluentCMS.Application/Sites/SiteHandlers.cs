using FluentCMS.Entities.Sites;
using FluentCMS.Services;
using MediatR;

namespace FluentCMS.Application.Sites;
internal class SiteHandlers :
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

    public async Task Handle(RemoveUrlSiteCommand request, CancellationToken cancellationToken)
    {
        await _siteService.Delete(request.Id);
    }

    public async Task Handle(AddUrlSiteCommand request, CancellationToken cancellationToken)
    {
        var site = await _siteService.GetById(request.Id);
        site.AddUrl(request.NewUrl);
        await _siteService.Update(site);
    }

    public async Task<Guid> Handle(DeleteSiteCommand request, CancellationToken cancellationToken)
    {
        await _siteService.Delete(request.Id);
        return request.Id;
    }

    public async Task<Guid> Handle(EditSiteCommand request, CancellationToken cancellationToken)
    {
        var site = await _siteService.GetById(request.Id);
        if (site.Name != request.Name) site.SetName(request.Name);
        if (site.Description != request.Description) site.SetDescription(request.Description);
        await _siteService.Update(site);
        return request.Id;
    }

    public async Task<Guid> Handle(CreateSiteCommand request, CancellationToken cancellationToken)
    {
        var newId = Guid.NewGuid();
        var site = new Site(newId, request.Name, request.Description, request.URLs, request.RoleId);
        await _siteService.Create(site);
        return newId;
    }

    public async Task<IEnumerable<Site>> Handle(GetSitesQuery request, CancellationToken cancellationToken)
    {
        return await _siteService.GetAll();
    }

    public async Task<Site> Handle(GetSiteByIdQuery request, CancellationToken cancellationToken)
    {
        return await _siteService.GetById(request.Id);
    }

    public async Task<Site> Handle(GetSiteByUrl request, CancellationToken cancellationToken)
    {
        return await _siteService.GetByUrl(request.Url);

    }
}
