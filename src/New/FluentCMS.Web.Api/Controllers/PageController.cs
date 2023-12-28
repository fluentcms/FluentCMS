using FluentCMS.Web.Api.Filters;

namespace FluentCMS.Web.Api.Controllers;

public class PageController(ISiteService siteService, IPageService pageService, ILayoutService layoutService, IMapper mapper) : BaseGlobalController
{

    [HttpGet("{siteUrl}")]
    [DecodeQueryParam]
    public async Task<IApiPagingResult<PageDetailResponse>> GetAll([FromRoute] string siteUrl, CancellationToken cancellationToken = default)
    {
        var site = await siteService.GetByUrl(siteUrl, cancellationToken);
        var entities = await pageService.GetBySiteId(site.Id, cancellationToken);
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

    [HttpGet("{siteUrl}/{path}")]
    [DecodeQueryParam]
    public async Task<IApiResult<PageFullDetailResponse>> GetByPath([FromRoute] string siteUrl, [FromRoute] string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(path))
            path = "/";

        if (!path.StartsWith("/"))
            path = "/" + path;

        var site = await siteService.GetByUrl(siteUrl, cancellationToken);
        var page = await pageService.GetByPath(site.Id, path, cancellationToken);
        var pageResponse = mapper.Map<PageFullDetailResponse>(page);
        pageResponse.Site = mapper.Map<SiteFullDetailResponse>(site);
        if (page.LayoutId.HasValue)
        {
            var layout = await layoutService.GetById(site.Id, page.LayoutId.Value, cancellationToken);
            pageResponse.Layout = mapper.Map<LayoutDetailResponse>(layout);
        }
        else
        {
            var layouts = await layoutService.GetAll(site.Id, cancellationToken);
            var layout = layouts.Where(l => l.IsDefault).First();
            pageResponse.Layout = mapper.Map<LayoutDetailResponse>(layout);
        }
        return Ok(pageResponse);
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
        return Ok(true);
    }
}
