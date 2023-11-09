using AutoMapper;
using FluentCMS.Api.Models;
using FluentCMS.Api.Models.Pages;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

public class PagesController : BaseController
{
    private readonly IPageService _pageService;
    private readonly IMapper _mapper;

    public PagesController(IPageService pageService, IMapper mapper)
    {
        _pageService = pageService;
        _mapper = mapper;
    }

    // GetBy Site and ParentId
    [HttpPost]
    public async Task<IApiPagingResult<PageResponse>> GetAll([FromBody] PageSearchRequest request)
    {
        var pages = await _pageService.GetBySiteIdAndParentId(request.SiteId, request.ParentId);
        return new ApiPagingResult<PageResponse>(_mapper.Map<List<PageResponse>>(pages));
    }

    [HttpGet("{id}")]
    public async Task<IApiResult<PageResponse>> GetById([FromRoute] Guid id)
    {
        var page = await _pageService.GetById(id);
        var pageResponse = _mapper.Map<PageResponse>(page);
        // map recursive?
        await MapChildren(id, pageResponse);
        return new ApiResult<PageResponse>(pageResponse);
    }

    private async Task MapChildren(Guid id, PageResponse pageResponse)
    {
        var childrenPage = await _pageService.GetByParentId(id);
        pageResponse.Children = childrenPage.Select(x => _mapper.Map<PageResponse>(x));
    }

    [HttpPost]
    public async Task<IApiResult<PageResponse>> Create(CreatePageRequest request)
    {
        var page = _mapper.Map<Page>(request);
        var newPage = await _pageService.Create(page);
        var pageResponse = _mapper.Map<PageResponse>(newPage);
        return new ApiResult<PageResponse>(pageResponse);
    }

    [HttpPut]
    public async Task<IApiResult<PageResponse>> Edit(EditPageRequest request)
    {
        var page = _mapper.Map<Page>(request);
        var updatedPage = await _pageService.Edit(page);
        var pageResponse = _mapper.Map<PageResponse>(updatedPage);
        return new ApiResult<PageResponse>(pageResponse);
    }

    [HttpDelete("{id}")]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id)
    {
        var page = await _pageService.GetById(id);
        await _pageService.Delete(page);
        return new ApiResult<bool>(true);
    }
}
