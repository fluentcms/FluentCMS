namespace FluentCMS.Web.Api.Controllers;

public class SiteController(ISiteService siteService, IPageService pageService, IMapper mapper) : BaseGlobalController
{
    public const string AREA = "Site Management";
    public const string READ = "Read";
    public const string UPDATE = $"Update/{READ}";
    public const string CREATE = "Create";
    public const string DELETE = $"Delete/{READ}";

    [PolicyAll]
    [HttpGet("{siteUrl}")]
    [Policy(AREA, READ)]
    public async Task<IApiResult<SiteDetailResponse>> GetByUrl([FromRoute] string siteUrl, CancellationToken cancellationToken = default)
    {
        var site = await siteService.GetByUrl(siteUrl, cancellationToken);
        var siteResponse = mapper.Map<SiteDetailResponse>(site);
        return Ok(siteResponse);
    }

    [HttpGet]
    [Policy(AREA, READ)]
    public async Task<IApiPagingResult<SiteDetailResponse>> GetAll(CancellationToken cancellationToken = default)
    {
        var entities = await siteService.GetAll(cancellationToken);
        var entitiesResponse = mapper.Map<List<SiteDetailResponse>>(entities);
        return OkPaged(entitiesResponse);
    }

    [HttpGet("{id}")]
    [Policy(AREA, READ)]
    public async Task<IApiResult<SiteDetailResponse>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var site = await siteService.GetById(id, cancellationToken);
        var siteResponse = mapper.Map<SiteDetailResponse>(site);
        return Ok(siteResponse);
    }

    [HttpPost]
    [Policy(AREA, CREATE)]
    public async Task<IApiResult<SiteDetailResponse>> Create([FromBody] SiteCreateRequest request, CancellationToken cancellationToken = default)
    {
        // creating new site
        var site = mapper.Map<Site>(request);
        var newSite = await siteService.Create(site, cancellationToken);

        // creating default page for the site
        var page = new Page
        {
            Title = "Default Page",
            Path = "/",
            Order = 0,
            SiteId = newSite.Id
        };
        await pageService.Create(page, cancellationToken);

        var response = mapper.Map<SiteDetailResponse>(newSite);

        return Ok(response);
    }

    [HttpPut]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<SiteDetailResponse>> Update([FromBody] SiteUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var entity = mapper.Map<Site>(request);
        var updated = await siteService.Update(entity, cancellationToken);
        var entityResponse = mapper.Map<SiteDetailResponse>(updated);
        return Ok(entityResponse);
    }

    [HttpDelete("{siteId}")]
    [Policy(AREA, DELETE)]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid siteId, CancellationToken cancellationToken = default)
    {
        await siteService.Delete(siteId, cancellationToken);
        return Ok(true);
    }
}
