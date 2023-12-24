using FluentCMS.Web.Api.Filters;

namespace FluentCMS.Web.Api.Controllers;

public class PageController(IPageService pageService, IMapper mapper) : BaseGlobalController
{

    [HttpGet("{siteId}")]
    public async Task<IApiPagingResult<PageDetailResponse>> GetAll([FromRoute] Guid siteId, CancellationToken cancellationToken = default)
    {
        var entities = await pageService.GetBySiteId(siteId, cancellationToken);
        var entitiesResponse = mapper.Map<List<PageDetailResponse>>(entities.ToList());
        return OkPaged(entitiesResponse);
    }

    [HttpGet("{id}")]
    public async Task<IApiResult<PageDetailResponse>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await pageService.GetById(id, cancellationToken);
        var entityResponse = mapper.Map<PageDetailResponse>(entity);
        return Ok(entityResponse);
    }

    [HttpGet("{siteId}/{path}")]
    [DecodeQueryParam]
    public async Task<IApiResult<PageDetailResponse>> GetByPath([FromRoute] Guid siteId, [FromRoute] string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(path))
            path = "/";

        if (!path.StartsWith("/"))
            path = "/" + path;

        var entity = await pageService.GetByPath(siteId, path, cancellationToken);
        var entityResponse = mapper.Map<PageDetailResponse>(entity);
        return Ok(entityResponse);
    }

    [HttpPost]
    public async Task<IApiResult<PageDetailResponse>> Create(PageCreateRequest request, CancellationToken cancellationToken = default)
    {
        var entity = mapper.Map<Page>(request);
        var newEntity = await pageService.Create(entity, cancellationToken);
        var pageResponse = mapper.Map<PageDetailResponse>(newEntity);
        return Ok(pageResponse);
    }

    [HttpPut]
    public async Task<IApiResult<PageDetailResponse>> Update(PageUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var entity = mapper.Map<Page>(request);
        var updatedEntity = await pageService.Update(entity, cancellationToken);
        var entityResponse = mapper.Map<PageDetailResponse>(updatedEntity);
        return Ok(entityResponse);
    }

    [HttpDelete("{id}")]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id)
    {
        await pageService.Delete(id);
        return new ApiResult<bool>(true);
    }
}
