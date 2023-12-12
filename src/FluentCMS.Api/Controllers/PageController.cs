using AutoMapper;
using FluentCMS.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

public class PageController(
    IPageService pageService,
    IPluginService pluginService,
    IPluginDefinitionService pluginDefinitionService,
    ILayoutService layoutService,
    IMapper mapper) : BaseController
{

    private readonly IPageService _pageService = pageService;
    private readonly IMapper _mapper = mapper;

    // GetBy Site and ParentId
    [HttpPost]
    public async Task<IApiPagingResult<PageResponse>> GetAll([FromBody] PageSearchRequest request)
    {
        var pages = await _pageService.GetBySiteId(request.SiteId);
        return new ApiPagingResult<PageResponse>(_mapper.Map<List<PageResponse>>(pages.ToList()));
    }

    [HttpGet("{id}")]
    public async Task<IApiResult<PageResponse>> GetById([FromRoute] Guid id)
    {
        var page = await _pageService.GetById(id);
        var pageResponse = _mapper.Map<PageResponse>(page);
        return new ApiResult<PageResponse>(pageResponse);
    }

    [HttpGet]
    public async Task<IApiResult<PageResponse>> GetByPath([FromQuery] Guid siteId, [FromQuery] string? path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(path))
            path = "/";

        if (!path.StartsWith("/"))
            path = "/" + path;

        var page = await _pageService.GetByPath(siteId, path, cancellationToken);
        var pageResponse = _mapper.Map<PageResponse>(page);
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

    [HttpPost]
    public async Task<IApiResult<PageResponse>> Create(PageCreateRequest request)
    {
        var page = _mapper.Map<Page>(request);
        var newPage = await _pageService.Create(page);
        var pageResponse = _mapper.Map<PageResponse>(newPage);
        return new ApiResult<PageResponse>(pageResponse);
    }

    [HttpPut]
    public async Task<IApiResult<PageResponse>> Update(PageUpdateRequest request)
    {
        var page = _mapper.Map<Page>(request);
        var updatedPage = await _pageService.Update(page);
        var pageResponse = _mapper.Map<PageResponse>(updatedPage);
        return new ApiResult<PageResponse>(pageResponse);
    }

    [HttpDelete("{id}")]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id)
    {
        await _pageService.Delete(id);
        return new ApiResult<bool>(true);
    }
}
