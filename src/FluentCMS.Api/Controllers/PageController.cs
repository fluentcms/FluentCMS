﻿using AutoMapper;
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

    // GetBy Site and ParentId
    [HttpPost]
    public async Task<IApiPagingResult<PageResponse>> GetAll([FromBody] PageSearchRequest request, CancellationToken cancellationToken = default)
    {
        var pages = await pageService.GetBySiteId(request.SiteId, cancellationToken);
        return new ApiPagingResult<PageResponse>(mapper.Map<List<PageResponse>>(pages.ToList()));
    }

    [HttpGet("{id}")]
    public async Task<IApiResult<PageResponse>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var page = await pageService.GetById(id, cancellationToken);
        var pageResponse = mapper.Map<PageResponse>(page);
        return new ApiResult<PageResponse>(pageResponse);
    }

    [HttpGet]
    public async Task<IApiResult<PageResponse>> GetByPath([FromQuery] Guid siteId, [FromQuery] string? path, CancellationToken cancellationToken = default)
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
