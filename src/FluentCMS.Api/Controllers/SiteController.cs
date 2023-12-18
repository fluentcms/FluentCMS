using AutoMapper;
using FluentCMS.Api.Filters;
using FluentCMS.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

/// <summary>
/// API controller for managing site entities in the FluentCMS system.
/// Provides actions for retrieving, creating, updating, and deleting sites.
/// </summary>
public class SiteController(
    ISiteService siteService,
    IPageService pageService,
    ILayoutService layoutService,
    IMapper mapper) : BaseController
{

    /// <summary>
    /// Retrieves all site entities.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A paginated result containing site entities.</returns>
    [HttpGet]
    public async Task<IApiPagingResult<SiteResponse>> GetAll(CancellationToken cancellationToken = default)
    {
        var sites = await siteService.GetAll(cancellationToken);
        var siteResponses = mapper.Map<List<SiteResponse>>(sites);
        return new ApiPagingResult<SiteResponse>(siteResponses);
    }

    /// <summary>
    /// Retrieves a specific site entity by its identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the site to retrieve.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The requested site entity.</returns>
    [HttpGet("{id}")]
    public async Task<IApiResult<SiteResponse>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var site = await siteService.GetById(id, cancellationToken);
        var pages = await pageService.GetBySiteId(site.Id, cancellationToken);

        var siteResponse = mapper.Map<SiteResponse>(site);
        siteResponse.Pages = mapper.Map<List<PageResponse>>(pages);

        return new ApiResult<SiteResponse>(siteResponse);
    }

    /// <summary>
    /// Retrieves a specific site entity by its URL.
    /// </summary>
    /// <param name="url">The URL of the site to retrieve.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The requested site entity.</returns>
    [HttpGet("{url}")]
    [DecodeQueryParam]
    public async Task<IApiResult<SiteResponse>> GetByUrl([FromRoute] string url, CancellationToken cancellationToken = default)
    {
        // TODO: should we change Url to domain?
        var site = await siteService.GetByUrl(url, cancellationToken);
        var pages = await pageService.GetBySiteId(site.Id, cancellationToken);

        var siteResponse = mapper.Map<SiteResponse>(site);
        siteResponse.Pages = mapper.Map<List<PageResponse>>(pages);

        siteResponse.Layout = await layoutService.GetById(site.Id, site.DefaultLayoutId, cancellationToken);

        return new ApiResult<SiteResponse>(siteResponse);
    }

    /// <summary>
    /// Creates a new site entity in the system.
    /// </summary>
    /// <param name="request">The site creation request data.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The newly created site entity.</returns>
    [HttpPost]
    public async Task<IApiResult<SiteResponse>> Create([FromBody] SiteCreateRequest request, CancellationToken cancellationToken = default)
    {
        var site = mapper.Map<Site>(request);
        var newSite = await siteService.Create(site, null, cancellationToken);
        var siteResponse = mapper.Map<SiteResponse>(newSite);
        return new ApiResult<SiteResponse>(siteResponse);
    }

    /// <summary>
    /// Updates an existing site entity in the system.
    /// </summary>
    /// <param name="request">The site update request data containing the new site details.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The updated site entity.</returns>
    [HttpPut]
    public async Task<IApiResult<SiteResponse>> Update([FromBody] SiteUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var site = mapper.Map<Site>(request);
        var updateSite = await siteService.Update(site, cancellationToken);
        var siteResponse = mapper.Map<SiteResponse>(updateSite);
        return new ApiResult<SiteResponse>(siteResponse);
    }

    /// <summary>
    /// Deletes a site entity from the system.
    /// </summary>
    /// <param name="id">The unique identifier of the site to be deleted.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An API result indicating the success of the deletion operation.</returns>
    [HttpDelete("{id}")]
    public async Task<IApiResult> Delete([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        // TODO: we should avoid calling GetById twice, first one is in the service, te other one in here
        var site = await siteService.GetById(id);
        await siteService.Delete(site, cancellationToken);
        return new ApiResult();
    }
}
