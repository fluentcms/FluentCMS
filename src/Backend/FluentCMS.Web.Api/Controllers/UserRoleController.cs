namespace FluentCMS.Web.Api.Controllers;

public class UserRoleController(IUserRoleService userRoleService, IRoleService roleService) : BaseGlobalController
{
    public const string AREA = "User Role Management";
    public const string READ = "Read";
    public const string UPDATE = $"Update/{READ}";

    [HttpGet]
    [Policy(AREA, READ)]
    public async Task<IApiResult<IEnumerable<UserRoleDetailResponse>>> GetUserRoles([FromQuery] Guid userId, Guid siteId, CancellationToken cancellationToken = default)
    {
        var userRoleIds = await userRoleService.GetUserRoleIds(userId, siteId, cancellationToken);
        var siteRoles = await roleService.GetAllForSite(siteId, cancellationToken);

        var userRoles = siteRoles.Select(x => new UserRoleDetailResponse
        {
            RoleName = x.Name,
            RoleId = x.Id,
            HasAccess = userRoleIds.Contains(x.Id)
        }).ToList();

        return Ok<IEnumerable<UserRoleDetailResponse>>(userRoles);
    }

    [HttpPut]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<bool>> Update([FromBody] UserRoleUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var result = await userRoleService.Update(request.UserId, request.SiteId, request.RoleIds, cancellationToken);

        return Ok(result);
    }
}
