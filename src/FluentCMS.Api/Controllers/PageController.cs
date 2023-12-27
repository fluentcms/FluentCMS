using AutoMapper;
using FluentCMS.Api.Filters;
using FluentCMS.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

/// <summary>
/// API controller for managing page entities in the FluentCMS system.
/// Provides actions for retrieving, creating, updating, and deleting pages.
/// </summary>
public class PageController(
    IPageService pageService,
    IPluginService pluginService,
    IPluginDefinitionService pluginDefinitionService,
    ILayoutService layoutService,
    IMapper mapper) : BaseController
{

    /// <summary>
    /// Retrieves all pages associated with a specific site.
    /// </summary>
    /// <param name="siteId">The unique identifier of the site.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A paginated result containing pages of the specified site.</returns>
    [HttpGet("{siteId}")]
    public async Task<IApiPagingResult<PageResponse>> GetAll([FromRoute] Guid siteId, CancellationToken cancellationToken = default)
    {
        var pages = await pageService.GetBySiteId(siteId, cancellationToken);
        return new ApiPagingResult<PageResponse>(mapper.Map<List<PageResponse>>(pages.ToList()));
    }

    /// <summary>
    /// Retrieves a specific page by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the page to retrieve.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An API result containing the requested page's data.</returns>
    [HttpGet("{id}")]
    public async Task<IApiResult<PageResponse>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var page = await pageService.GetById(id, cancellationToken);
        var pageResponse = mapper.Map<PageResponse>(page);
        return new ApiResult<PageResponse>(pageResponse);
    }

    /// <summary>
    /// Retrieves a specific page by its path within a site.
    /// </summary>
    /// <param name="siteId">The unique identifier of the site to which the page belongs.</param>
    /// <param name="path">The URL path of the page to retrieve.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An API result containing the requested page's data along with its associated plugins and layout.</returns>
    /// <remarks>
    /// This method also applies URL decoding to the 'path' parameter to handle any URL-encoded characters.
    /// </remarks>
    [HttpGet("{siteId}/{path}")]
    [DecodeQueryParam]
    public async Task<IApiResult<PageResponse>> GetByPath([FromRoute] Guid siteId, [FromRoute] string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(path))
            path = "/";

        if (!path.StartsWith("/"))
            path = "/" + path;

        var page = await pageService.GetByPath(siteId, path, cancellationToken);
        var pageResponse = mapper.Map<PageResponse>(page);
        var pagePlugins = await pluginService.GetByPageId(page.Id, cancellationToken);
        var pluginDefinitions = await pluginDefinitionService.GetAll(cancellationToken);
        var layouts = await layoutService.GetAll(siteId, cancellationToken);

        pageResponse.Plugins = pagePlugins.Select(p => new PluginResponse
        {
            Id = p.Id,
            Definition = pluginDefinitions.Single(d => d.Id == p.DefinitionId),
            Order = p.Order,
            Section = p.Section,
        }).ToList();

        if (page.LayoutId != null)
        {
            pageResponse.Layout = layouts.Where(x => x.Id == page.LayoutId.Value).Single();
        }

        return new ApiResult<PageResponse>(pageResponse);
    }

    /// <summary>
    /// Creates a new page in the FluentCMS system.
    /// </summary>
    /// <param name="request">The page creation request data containing the details of the new page.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An API result containing the newly created page's data.</returns>
    [HttpPost]
    public async Task<IApiResult<PageResponse>> Create(PageCreateRequest request, CancellationToken cancellationToken = default)
    {
        var page = mapper.Map<Page>(request);
        var newPage = await pageService.Create(page, cancellationToken);
        var pageResponse = mapper.Map<PageResponse>(newPage);
        return new ApiResult<PageResponse>(pageResponse);
    }

    [HttpPut]
    public async Task<IApiResult<PageResponse>> Update(PageUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var page = mapper.Map<Page>(request);
        var updatedPage = await pageService.Update(page, cancellationToken);
        var pageResponse = mapper.Map<PageResponse>(updatedPage);
        return new ApiResult<PageResponse>(pageResponse);
    }

    [HttpDelete("{id}")]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id)
    {
        await pageService.Delete(id);
        return new ApiResult<bool>(true);
    }
}
