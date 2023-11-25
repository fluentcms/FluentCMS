using AutoMapper;
using FluentCMS.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

public class PageController : BaseController
{
    private readonly IPageService _pageService;
    private readonly IMapper _mapper;

    public PageController(IPageService pageService, IMapper mapper)
    {
        _pageService = pageService;
        _mapper = mapper;
    }

    // GetBy Site and ParentId
    [HttpPost]
    public async Task<IApiPagingResult<PageResponse>> GetAll([FromBody] PageSearchRequest request)
    {
        var pages = (await _pageService.GetBySiteId(request.SiteId));
        return new ApiPagingResult<PageResponse>(_mapper.Map<List<PageResponse>>(pages.ToList()));
    }

    [HttpGet("{id}")]
    public async Task<IApiResult<PageResponse>> GetById([FromRoute] Guid id)
    {
        var page = await _pageService.GetById(id);
        var pageResponse = _mapper.Map<PageResponse>(page);
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
