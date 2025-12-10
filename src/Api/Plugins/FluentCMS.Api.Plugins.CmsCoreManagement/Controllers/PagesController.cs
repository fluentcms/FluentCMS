namespace FluentCMS.Api.Plugins.CmsCoreManagement.Controllers;

public class PagesController(IPageService pageService) : BaseController
{
    
    [HttpGet]
    public async Task<ApiListResponse<PageDto>> GetBySiteId([FromQuery] Guid siteId, CancellationToken cancellationToken = default)
    {
        var pages = await pageService.GetBySiteId(siteId, cancellationToken);
        
        var pagesDto = Mapper.Map<List<PageDto>>(pages);

        foreach (var page in pagesDto)
            page.FullPath = await pageService.GetPageUrl(page.Id, cancellationToken);

        return SuccessList(pagesDto);
    }

    [HttpGet]
    public async Task<ApiListResponse<PageDto>> GetAll(CancellationToken cancellationToken = default)
    {
        var pages = await pageService.GetAll(cancellationToken);
        var pagesDto = Mapper.Map<List<PageDto>>(pages);
        return SuccessList(pagesDto);
    }

    [HttpGet("{id:guid}")]
    public async Task<ApiResponse<PageDto>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var page = await pageService.GetById(id, cancellationToken);
        return Success(Mapper.Map<PageDto>(page));
    }

    [HttpPost]
    public async Task<ApiResponse<PageDto>> Add(PageAddRequest request, CancellationToken cancellationToken = default)
    {
        var page = Mapper.Map<Page>(request);
        await pageService.Add(page, cancellationToken);
        return Success(Mapper.Map<PageDto>(page));
    }

    [HttpPut("{id:guid}")]
    public async Task<ApiResponse<PageDto>> Update(Guid id, PageUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var page = await pageService.GetById(id, cancellationToken);
        Mapper.Map(request, page);
        await pageService.Update(page, cancellationToken);
        return Success(Mapper.Map<PageDto>(page));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ApiResponse> Remove(Guid id, CancellationToken cancellationToken = default)
    {
        await pageService.Remove(id, cancellationToken);
        return Success();
    }
}
