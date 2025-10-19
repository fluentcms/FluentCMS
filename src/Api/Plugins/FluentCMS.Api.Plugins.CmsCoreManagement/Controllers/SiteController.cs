namespace FluentCMS.Api.Plugins.CmsCoreManagement.Controllers;

public class SiteController(ISiteService siteService) : BaseController
{
    [HttpGet]
    public async Task<ApiListResponse<SiteDto>> GetAll(CancellationToken cancellationToken = default)
    {
        var sites = await siteService.GetAll(cancellationToken);
        var sitesDto = Mapper.Map<List<SiteDto>>(sites);
        return SuccessList(sitesDto);
    }

    [HttpGet("{id:guid}")]
    public async Task<ApiResponse<SiteDto>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var site = await siteService.GetById(id, cancellationToken);
        return Success(Mapper.Map<SiteDto>(site));
    }

    [HttpPost]
    public async Task<ApiResponse<SiteDto>> Add(SiteAddRequest request, CancellationToken cancellationToken = default)
    {
        var site = Mapper.Map<Site>(request);
        await siteService.Add(site, cancellationToken);
        return Success(Mapper.Map<SiteDto>(site));
    }

    [HttpPut("{id:guid}")]
    public async Task<ApiResponse<SiteDto>> Update(Guid id, SiteUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var site = await siteService.GetById(id, cancellationToken);
        Mapper.Map(request, site);
        await siteService.Update(site, cancellationToken);
        return Success(Mapper.Map<SiteDto>(site));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ApiResponse> Remove(Guid id, CancellationToken cancellationToken = default)
    {
        await siteService.Remove(id, cancellationToken);
        return Success();
    }
}
