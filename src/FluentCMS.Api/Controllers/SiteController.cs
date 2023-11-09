using AutoMapper;
using FluentCMS.Api.Models;
using FluentCMS.Api.Models.Pages;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

public class SiteController : BaseController
{
    private readonly ISiteService _siteService;
    private readonly IPageService _pageService;
    private readonly IMapper _mapper;

    public SiteController(ISiteService siteService, IPageService pageService, IMapper mapper)
    {
        _siteService = siteService;
        _pageService = pageService;
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<IApiPagingResult<SiteResponse>> GetAll([FromQuery] SearchSiteRequest request, CancellationToken cancellationToken = default)
    {
        var sites = await _siteService.GetAll(cancellationToken);
        var siteResponses = _mapper.Map<List<SiteResponse>>(sites);
        return new ApiPagingResult<SiteResponse>(siteResponses);
    }

    [HttpGet("{id}")]
    public async Task<IApiResult<SiteResponse>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var site = await _siteService.GetById(id, cancellationToken);
        var pages = await _pageService.GetBySiteId(site.Id, cancellationToken);

        var siteResponse = _mapper.Map<SiteResponse>(site);
        siteResponse.Pages = _mapper.Map<List<PageResponse>>(pages);

        return new ApiResult<SiteResponse>(siteResponse);
    }

    [HttpGet]
    public async Task<IApiResult<SiteResponse>> GetByUrl([FromQuery] string url, CancellationToken cancellationToken = default)
    {
        // TODO: should we change Url to domain?
        var site = await _siteService.GetByUrl(url, cancellationToken);
        var pages = await _pageService.GetBySiteId(site.Id, cancellationToken);

        var siteResponse = _mapper.Map<SiteResponse>(site);
        siteResponse.Pages = _mapper.Map<List<PageResponse>>(pages);

        return new ApiResult<SiteResponse>(siteResponse);
    }

    [HttpPost]
    public async Task<IApiResult<SiteResponse>> Create(CreateSiteRequest request, CancellationToken cancellationToken = default)
    {
        var site = _mapper.Map<Site>(request);
        var newSite = await _siteService.Create(site, cancellationToken);
        var siteResponse = _mapper.Map<SiteResponse>(newSite);
        return new ApiResult<SiteResponse>(siteResponse);
    }

    [HttpPatch]
    public async Task<IApiResult<SiteResponse>> Update(UpdateSiteRequest request, CancellationToken cancellationToken = default)
    {
        var site = _mapper.Map<Site>(request);
        var updateSite = await _siteService.Update(site, cancellationToken);
        var siteResponse = _mapper.Map<SiteResponse>(updateSite);
        return new ApiResult<SiteResponse>(siteResponse);
    }

    [HttpDelete("{id}")]
    public async Task<IApiResult> Delete([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        // TODO: we should avoid calling GetById twice, first one is in the service, te other one in here
        var site = await _siteService.GetById(id);
        await _siteService.Delete(site, cancellationToken);
        return new ApiResult();
    }
}
