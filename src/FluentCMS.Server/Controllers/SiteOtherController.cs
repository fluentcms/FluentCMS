using AutoMapper;
using FluentCMS.Application.Services;
using FluentCMS.Entities;
using FluentCMS.Server.Models;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Server.Controllers;

public class SiteOtherController : BaseController
{
    private readonly ISiteOtherService _siteOtherService;
    private readonly IPageService _pageService;
    private readonly IMapper _mapper;

    public SiteOtherController(ISiteOtherService siteService, IPageService pageService, IMapper mapper)
    {
        _siteOtherService = siteService;
        _pageService = pageService;
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<IApiListResult<SiteResponse>> GetAll(CancellationToken cancellationToken = default)
    {
        var sites = await _siteOtherService.GetAll(cancellationToken);
        var siteResponses = _mapper.Map<List<SiteResponse>>(sites);
        return new ApiListResult<SiteResponse>(siteResponses);
    }

    [HttpGet("{id}")]
    public async Task<IApiResult<SiteResponse>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var site = await _siteOtherService.GetById(id, cancellationToken);
        var pages = await _pageService.GetBySiteId(site.Id, cancellationToken);

        var siteResponse = _mapper.Map<SiteResponse>(site);
        siteResponse.Pages = _mapper.Map<List<PageResponse>>(pages);

        return new ApiResult<SiteResponse>(siteResponse);
    }

    [HttpGet]
    public async Task<IApiResult<SiteResponse>> GetByUrl([FromQuery] string url, CancellationToken cancellationToken = default)
    {
        // TODO: should we change Url to domain?
        var site = await _siteOtherService.GetByUrl(url, cancellationToken);
        var pages = await _pageService.GetBySiteId(site.Id, cancellationToken);

        var siteResponse = _mapper.Map<SiteResponse>(site);
        siteResponse.Pages = _mapper.Map<List<PageResponse>>(pages);

        return new ApiResult<SiteResponse>(siteResponse);
    }

    [HttpPost]
    public async Task<IApiResult<SiteResponse>> Create(SiteCreateRequest request, CancellationToken cancellationToken = default)
    {
        var site = _mapper.Map<Site>(request);
        var newSite = await _siteOtherService.Create(site, cancellationToken);
        var siteResponse = _mapper.Map<SiteResponse>(newSite);
        return new ApiResult<SiteResponse>(siteResponse);
    }

    [HttpPatch]
    public async Task<IApiResult<SiteResponse>> Update(SiteUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var site = _mapper.Map<Site>(request);
        var updateSite = await _siteOtherService.Update(site, cancellationToken);
        var siteResponse = _mapper.Map<SiteResponse>(updateSite);
        return new ApiResult<SiteResponse>(siteResponse);
    }

    [HttpDelete("{id}")]
    public async Task<IApiResult> Delete([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        await _siteOtherService.Delete(id, cancellationToken);
        return new ApiResult();
    }

}
