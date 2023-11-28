using AutoMapper;
using FluentCMS.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

public class SiteController(
    ISiteService siteService,
    IPageService pageService,
    ILayoutService layoutService,
    IMapper mapper) : BaseController
{

    [HttpGet]
    public async Task<IApiPagingResult<SiteResponse>> GetAll([FromQuery] SiteSearchRequest request, CancellationToken cancellationToken = default)
    {
        var sites = await siteService.GetAll(cancellationToken);
        var siteResponses = mapper.Map<List<SiteResponse>>(sites);
        return new ApiPagingResult<SiteResponse>(siteResponses);
    }

    [HttpGet("{id}")]
    public async Task<IApiResult<SiteResponse>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var site = await siteService.GetById(id, cancellationToken);
        var pages = await pageService.GetBySiteId(site.Id, cancellationToken);

        var siteResponse = mapper.Map<SiteResponse>(site);
        siteResponse.Pages = mapper.Map<List<PageResponse>>(pages);

        return new ApiResult<SiteResponse>(siteResponse);
    }

    [HttpGet]
    public async Task<IApiResult<SiteResponse>> GetByUrl([FromQuery] string url, CancellationToken cancellationToken = default)
    {
        // TODO: should we change Url to domain?
        var site = await siteService.GetByUrl(url, cancellationToken);
        var pages = await pageService.GetBySiteId(site.Id, cancellationToken);

        var siteResponse = mapper.Map<SiteResponse>(site);
        siteResponse.Pages = mapper.Map<List<PageResponse>>(pages);

        siteResponse.Layout = await layoutService.GetById(site.Id, site.DefaultLayoutId, cancellationToken);

        return new ApiResult<SiteResponse>(siteResponse);
    }

    [HttpPost]
    public async Task<IApiResult<SiteResponse>> Create(SiteCreateRequest request, CancellationToken cancellationToken = default)
    {
        var site = mapper.Map<Site>(request);
        var newSite = await siteService.Create(site, null, cancellationToken);
        var siteResponse = mapper.Map<SiteResponse>(newSite);
        return new ApiResult<SiteResponse>(siteResponse);
    }

    [HttpPatch]
    public async Task<IApiResult<SiteResponse>> Update(SiteUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var site = mapper.Map<Site>(request);
        var updateSite = await siteService.Update(site, cancellationToken);
        var siteResponse = mapper.Map<SiteResponse>(updateSite);
        return new ApiResult<SiteResponse>(siteResponse);
    }

    [HttpDelete("{id}")]
    public async Task<IApiResult> Delete([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        // TODO: we should avoid calling GetById twice, first one is in the service, te other one in here
        var site = await siteService.GetById(id);
        await siteService.Delete(site, cancellationToken);
        return new ApiResult();
    }
}
