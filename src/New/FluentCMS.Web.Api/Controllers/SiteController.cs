namespace FluentCMS.Web.Api.Controllers;

public class SiteController(ISiteService siteService, ILayoutService layoutService, IPageService pageService, IMapper mapper) : BaseGlobalController
{
    [HttpGet]
    public async Task<IApiPagingResult<SiteDetailResponse>> GetAll(CancellationToken cancellationToken = default)
    {
        var entities = await siteService.GetAll(cancellationToken);
        var entitiesResponse = mapper.Map<List<SiteDetailResponse>>(entities);
        return OkPaged(entitiesResponse);
    }

    [HttpGet("{id}")]
    public async Task<IApiResult<SiteFullDetailResponse>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var site = await siteService.GetById(id, cancellationToken);
        var siteResponse = mapper.Map<SiteFullDetailResponse>(site);
        var layouts = await layoutService.GetAll(id, cancellationToken);
        siteResponse.Layouts = mapper.Map<List<LayoutDetailResponse>>(layouts);
        return Ok(siteResponse);
    }

    [HttpPost]
    public async Task<IApiResult<SiteDetailResponse>> Create([FromBody] SiteCreateRequest request, CancellationToken cancellationToken = default)
    {
        // creating new site
        var site = mapper.Map<Site>(request);
        var newSite = await siteService.Create(site, cancellationToken);

        // creating default page for the site
        var page = new Page
        {
            Title = request.DefaultPageTitle,
            Path = "/",
            Order = 0,
            SiteId = newSite.Id
        };
        await pageService.Create(page, cancellationToken);

        // creating default layout for the site
        var layout = new Layout
        {
            Name = request.Name,
            SiteId = newSite.Id,
            Body = request.LayoutBody,
            Head = request.LayoutHead,
            IsDefault = true
        };
        await layoutService.Create(layout, cancellationToken);

        var response = mapper.Map<SiteDetailResponse>(newSite);

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
