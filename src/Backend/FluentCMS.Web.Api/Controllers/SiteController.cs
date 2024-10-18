using FluentCMS.Services.Models;
using FluentCMS.Services.Permissions;

namespace FluentCMS.Web.Api.Controllers;

public class SiteController(ISiteService siteService, ISettingsService settingsService, IPermissionService permissionService, IMapper mapper) : BaseGlobalController
{
    public const string AREA = "Site Management";
    public const string READ = "Read";
    public const string UPDATE = $"Update/{READ}";
    public const string CREATE = "Create";
    public const string DELETE = $"Delete/{READ}";

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
        siteResponse.Settings = (await settingsService.GetById(site.Id, cancellationToken)).Values;
        siteResponse.AdminRoleIds = (await permissionService.Get(site.Id, SitePermissionAction.SiteAdmin, cancellationToken)).Select(x => x.RoleId);
        siteResponse.ContributorRoleIds = (await permissionService.Get(site.Id, SitePermissionAction.SiteContributor, cancellationToken)).Select(x => x.RoleId);

        return Ok(siteResponse);
    }

    [HttpPost]
    [Policy(AREA, CREATE)]
    public async Task<IApiResult<SiteDetailResponse>> Create([FromBody] SiteCreateRequest request, CancellationToken cancellationToken = default)
    {
        var siteTemplate = mapper.Map<SiteTemplate>(request);
        var newSite = await siteService.Create(siteTemplate, cancellationToken);
        var response = mapper.Map<SiteDetailResponse>(newSite);
        return Ok(response);
    }

    [HttpPut]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<SiteDetailResponse>> Update([FromBody] SiteUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var entity = mapper.Map<Site>(request);
        var updated = await siteService.Update(entity, cancellationToken);
        await permissionService.Set(updated.Id, SitePermissionAction.SiteAdmin, request.AdminRoleIds, cancellationToken);
        await permissionService.Set(updated.Id, SitePermissionAction.SiteContributor, request.ContributorRoleIds, cancellationToken);

        var siteResponse = mapper.Map<SiteDetailResponse>(updated);
        siteResponse.Settings = (await settingsService.GetById(request.Id, cancellationToken)).Values;
        return Ok(siteResponse);
    }

    [HttpDelete("{siteId}")]
    [Policy(AREA, DELETE)]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid siteId, CancellationToken cancellationToken = default)
    {
        await siteService.Delete(siteId, cancellationToken);
        return Ok(true);
    }
}
