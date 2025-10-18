namespace FluentCMS.Api.Plugins.IdentityManagement.Controllers;

public class RolesController(IRoleService roleService, IUserRoleService userRoleService, IUserService userService) : BaseController
{
    //[HttpGet("{id}")]
    //public async Task<ApiResponse<RoleDto>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    //{
    //    var role = await roleService.GetById(id, cancellationToken);
    //    var roleDto = Mapper.Map<RoleDto>(role);
    //    return Success(roleDto);
    //}

    [HttpGet("{id:guid}")]
    public async Task<IApiResponse> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var role = await roleService.GetById(id, cancellationToken);
        return Success(role);
    }

    //[HttpPost]
    //public async Task<ActionResult<ApiResponse<RoleDto>>> Add([FromBody] RoleAddDto createRoleDto, CancellationToken cancellationToken = default)
    //{
    //    var role = Mapper.Map<Role>(createRoleDto);
    //    await roleService.Add(role, cancellationToken);
    //    var roleDto = Mapper.Map<RoleDto>(role);
    //    return Success(roleDto);
    //}

    //[HttpPut("{id:guid}")]
    //public async Task<ActionResult<ApiResponse<RoleDto>>> Update(Guid id, [FromBody] RoleUpdateDto updateRoleDto, CancellationToken cancellationToken = default)
    //{
    //    var role = await roleService.GetById(id, cancellationToken);

    //    Mapper.Map(updateRoleDto, role);
    //    await roleService.Update(role, cancellationToken);
    //    var roleDto = Mapper.Map<RoleDto>(role);
    //    return Success(roleDto);
    //}

    //[HttpDelete("{id:guid}")]
    //public async Task<ApiResponse> Remove(Guid id, CancellationToken cancellationToken = default)
    //{
    //    await roleService.Remove(id, cancellationToken);
    //    return NoContent();
    //}

    //[HttpGet("{id:guid}/users")]
    //public async Task<ActionResult<ApiListResponse<UserDto>>> GetRoleUsers(Guid id, CancellationToken cancellationToken = default)
    //{
    //    // Verify role exists
    //    var role = await roleService.GetById(id, cancellationToken);
    //    var siteId = SecurityContext.Site?.Id ?? throw new InvalidOperationException("Site context is required");

    //    // Get all users and filter by those who have this role
    //    var allUsers = await userService.GetAll(cancellationToken);
    //    var usersWithRole = new List<UserDto>();

    //    foreach (var user in allUsers)
    //    {
    //        var userRoles = await userRoleService.GetUserRoles(user.Id, siteId, cancellationToken);
    //        if (userRoles.Any(r => r.Id == id))
    //        {
    //            usersWithRole.Add(Mapper.Map<UserDto>(user));
    //        }
    //    }

    //    // For this endpoint, we'll return a simple list without complex pagination
    //    // since it's typically a smaller dataset
    //    var pagination = new PaginationInfo
    //    {
    //        Page = 1,
    //        PageSize = usersWithRole.Count,
    //        TotalPages = 1,
    //        TotalCount = usersWithRole.Count,
    //        HasNextPage = false,
    //        HasPreviousPage = false
    //    };

    //    return SuccessList(usersWithRole, pagination);
    //}

    //[HttpGet("/api/admin/users/{userId:guid}/roles")]
    //public async Task<ActionResult<ApiListResponse<RoleDto>>> GetUserRoles(Guid userId, CancellationToken cancellationToken = default)
    //{
    //    var siteId = SecurityContext.Site?.Id ?? throw new InvalidOperationException("Site context is required");

    //    // Verify user exists
    //    var user = await userService.GetById(userId, cancellationToken);

    //    // Get user's roles
    //    var userRoles = await userRoleService.GetUserRoles(userId, siteId, cancellationToken);
    //    var roleDtos = userRoles.Select(Mapper.Map<RoleDto>).ToList();

    //    // Simple pagination for user roles
    //    var pagination = new PaginationInfo
    //    {
    //        Page = 1,
    //        PageSize = roleDtos.Count,
    //        TotalPages = 1,
    //        TotalCount = roleDtos.Count,
    //        HasNextPage = false,
    //        HasPreviousPage = false
    //    };

    //    return SuccessList(roleDtos, pagination);
    //}

    //[HttpPost("/api/admin/users/{userId:guid}/roles")]
    //public async Task<ActionResult<ApiResponse>> AssignRolesToUser(
    //    Guid userId,
    //    [FromBody] AssignRolesDto assignRolesDto,
    //    CancellationToken cancellationToken = default)
    //{
    //    // Verify user exists
    //    var user = await userService.GetById(userId, cancellationToken);

    //    // Assign each role to the user
    //    foreach (var roleId in assignRolesDto.RoleIds)
    //    {
    //        await userRoleService.AssignUserToRole(userId, roleId, cancellationToken);
    //    }

    //    return NoContent();
    //}

    //[HttpDelete("/api/admin/users/{userId:guid}/roles/{roleId:guid}")]
    //public async Task<ActionResult<ApiResponse>> RemoveRoleFromUser(
    //    Guid userId,
    //    Guid roleId,
    //    CancellationToken cancellationToken = default)
    //{
    //    // Verify user exists
    //    var user = await userService.GetById(userId, cancellationToken);

    //    // Remove the role from the user
    //    await userRoleService.RemoveUserFromRole(userId, roleId, cancellationToken);

    //    return NoContent();
    //}

    //[HttpDelete("/api/admin/users/{userId:guid}/roles")]
    //public async Task<ActionResult<ApiResponse>> RemoveAllRolesFromUser(
    //    Guid userId,
    //    CancellationToken cancellationToken = default)
    //{
    //    var siteId = SecurityContext.Site?.Id ?? throw new InvalidOperationException("Site context is required");

    //    // Verify user exists
    //    var user = await userService.GetById(userId, cancellationToken);

    //    // Get all current roles for the user
    //    var userRoles = await userRoleService.GetUserRoles(userId, siteId, cancellationToken);

    //    // Remove each role from the user
    //    foreach (var role in userRoles)
    //    {
    //        await userRoleService.RemoveUserFromRole(userId, role.Id, cancellationToken);
    //    }

    //    return NoContent();
    //}

    //[HttpPut("/api/admin/users/{userId:guid}/roles")]
    //public async Task<ActionResult<ApiResponse>> ReplaceUserRoles(
    //    Guid userId,
    //    [FromBody] ReplaceUserRolesDto replaceUserRolesDto,
    //    CancellationToken cancellationToken = default)
    //{
    //    var siteId = SecurityContext.Site?.Id ?? throw new InvalidOperationException("Site context is required");

    //    // Verify user exists
    //    var user = await userService.GetById(userId, cancellationToken);

    //    // Get all current roles for the user
    //    var currentUserRoles = await userRoleService.GetUserRoles(userId, siteId, cancellationToken);

    //    // Remove all existing roles
    //    foreach (var role in currentUserRoles)
    //    {
    //        await userRoleService.RemoveUserFromRole(userId, role.Id, cancellationToken);
    //    }

    //    // Assign new roles
    //    foreach (var roleId in replaceUserRolesDto.RoleIds)
    //    {
    //        await userRoleService.AssignUserToRole(userId, roleId, cancellationToken);
    //    }

    //    return NoContent();
    //}
}
