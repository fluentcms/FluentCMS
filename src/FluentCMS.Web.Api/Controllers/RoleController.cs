namespace FluentCMS.Web.Api.Controllers;

public class RoleController(
    IMapper mapper,
    IRoleService roleService,
    IAppService appService) : BaseAppController
{
    [HttpGet]
    public async Task<IApiPagingResult<RoleDetailResponse>> GetAll([FromRoute] string appSlug, CancellationToken cancellationToken = default)
    {
        var app = await appService.GetBySlug(appSlug, cancellationToken);
        var roles = await roleService.GetAll(app.Id, cancellationToken);
        var roleResponses = mapper.Map<IEnumerable<RoleDetailResponse>>(roles);
        return OkPaged(roleResponses);
    }

    [HttpPost]
    public async Task<IApiResult<RoleDetailResponse>> Create([FromRoute] string appSlug, [FromBody] RoleCreateRequest request, CancellationToken cancellationToken = default)
    {
        var role = mapper.Map<Role>(request);
        var app = await appService.GetBySlug(appSlug, cancellationToken);
        role.AppId = app.Id;
        var newRole = await roleService.Create(role, cancellationToken);
        var roleResponse = mapper.Map<RoleDetailResponse>(newRole);
        return Ok(roleResponse);
    }

    [HttpPut]
    public async Task<IApiResult<RoleDetailResponse>> Update([FromRoute] string appSlug, [FromBody] RoleUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var role = mapper.Map<Role>(request);
        var app = await appService.GetBySlug(appSlug, cancellationToken);
        role.AppId = app.Id;
        var updated = await roleService.Update(role, cancellationToken);
        var roleResponse = mapper.Map<RoleDetailResponse>(updated);
        return Ok(roleResponse);
    }

    [HttpDelete("{id}")]
    public async Task<IApiResult<bool>> Delete([FromRoute] string appSlug, [FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var app = await appService.GetBySlug(appSlug, cancellationToken);
        await roleService.Delete(app.Id, id, cancellationToken);
        return Ok(true);
    }
}
