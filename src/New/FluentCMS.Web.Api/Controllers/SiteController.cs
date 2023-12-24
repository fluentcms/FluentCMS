namespace FluentCMS.Web.Api.Controllers;

public class SiteController(ISiteService siteService, IMapper mapper) : BaseGlobalController
{
    [HttpGet]
    public async Task<IApiPagingResult<SiteDetailResponse>> GetAll(CancellationToken cancellationToken = default)
    {
        var entities = await siteService.GetAll(cancellationToken);
        var entitiesResponse = mapper.Map<List<SiteDetailResponse>>(entities);
        return OkPaged(entitiesResponse);
    }

    [HttpGet("{url}")]
    public async Task<IApiResult<SiteDetailResponse>> GetByUrl([FromRoute] string url, CancellationToken cancellationToken = default)
    {
        var entity = await siteService.GetByUrl(url, cancellationToken);
        var entityResponse = mapper.Map<SiteDetailResponse>(entity);
        return Ok(entityResponse);
    }

    [HttpPost]
    public async Task<IApiResult<SiteDetailResponse>> Create([FromBody] SiteCreateRequest request, CancellationToken cancellationToken = default)
    {
        var entity = mapper.Map<Site>(request);
        var created = await siteService.Create(entity, cancellationToken);
        var response = mapper.Map<SiteDetailResponse>(created);
        return Ok(response);
    }

    [HttpPut]
    public async Task<IApiResult<SiteDetailResponse>> Update([FromBody] SiteUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var entity = mapper.Map<Site>(request);
        var updated = await siteService.Update(entity, cancellationToken);
        var entityResponse = mapper.Map<SiteDetailResponse>(updated);
        return Ok(entityResponse);
    }

    [HttpDelete("{siteId}")]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid siteId, CancellationToken cancellationToken = default)
    {
        await siteService.Delete(siteId, cancellationToken);
        return Ok(true);
    }
}
