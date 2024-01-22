namespace FluentCMS.Web.Api.Controllers;

public class AppController(IAppService appService, IMapper mapper) : BaseGlobalController
{

    [HttpPost]
    public async Task<IApiResult<AppDetailResponse>> Create([FromBody] AppCreateRequest request, CancellationToken cancellationToken = default)
    {
        var app = mapper.Map<App>(request);
        var created = await appService.Create(app, cancellationToken);
        var response = mapper.Map<AppDetailResponse>(created);
        return Ok(response);
    }

    [HttpPut]
    public async Task<IApiResult<AppDetailResponse>> Update([FromBody] AppUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var app = mapper.Map<App>(request);
        var updated = await appService.Update(app, cancellationToken);
        var appResponse = mapper.Map<AppDetailResponse>(updated);
        return Ok(appResponse);
    }

    [HttpGet]
    public async Task<IApiPagingResult<AppDetailResponse>> GetAll(CancellationToken cancellationToken = default)
    {
        var apps = await appService.GetAll(cancellationToken);
        var appsResponse = mapper.Map<List<AppDetailResponse>>(apps);
        return OkPaged(appsResponse);
    }

    [HttpDelete("{appId}")]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid appId, CancellationToken cancellationToken = default)
    {
        await appService.Delete(appId, cancellationToken);
        return Ok(true);
    }

    [HttpGet("{appSlug}")]
    public async Task<IApiResult<AppDetailResponse>> GetBySlug([FromRoute] string appSlug, CancellationToken cancellationToken = default)
    {
        var app = await appService.GetBySlug(appSlug, cancellationToken);
        var appResponse = mapper.Map<AppDetailResponse>(app);
        return Ok(appResponse);
    }
}
