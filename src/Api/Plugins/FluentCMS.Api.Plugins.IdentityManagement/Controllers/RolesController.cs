namespace FluentCMS.Api.Plugins.IdentityManagement.Controllers;

public class RolesController(IRoleService roleService) : BaseController
{
    [HttpGet("{id:guid}")]
    public async Task<ApiResponse<RoleDto>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var role = await roleService.GetById(id, cancellationToken);
        return Success(Mapper.Map<RoleDto>(role));
    }

    [HttpGet]
    public async Task<ApiListResponse<RoleDto>> GetAll(CancellationToken cancellationToken = default)
    {
        var roles = await roleService.GetAll(cancellationToken);
        var rolesDto = Mapper.Map<List<RoleDto>>(roles);
        return SuccessList(rolesDto);
    }

    [HttpPost]
    public async Task<ApiResponse<RoleDto>> Add([FromBody] RoleAddRequest createRoleDto, CancellationToken cancellationToken = default)
    {
        var role = Mapper.Map<Role>(createRoleDto);
        await roleService.Add(role, cancellationToken);
        return Success(Mapper.Map<RoleDto>(role));
    }

    [HttpPut("{id:guid}")]
    public async Task<ApiResponse<RoleDto>> Update(Guid id, [FromBody] RoleUpdateRequest updateRoleDto, CancellationToken cancellationToken = default)
    {
        var role = await roleService.GetById(id, cancellationToken);

        Mapper.Map(updateRoleDto, role);
        await roleService.Update(role, cancellationToken);
        return Success(Mapper.Map<RoleDto>(role));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ApiResponse> Remove(Guid id, CancellationToken cancellationToken = default)
    {
        await roleService.Remove(id, cancellationToken);
        return Success();
    }

    [HttpGet]
    public async Task<ApiListResponse<RoleDto>> GetUserRoles([FromQuery] Guid userId, CancellationToken cancellationToken = default)
    {
        var roles = await roleService.GetUserRoles(userId, cancellationToken);
        var roleDtos = Mapper.Map<List<RoleDto>>(roles);

        return SuccessList(roleDtos);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse>> UpdateUserRoles(UserRolesUpdateRequest request, CancellationToken cancellationToken = default)
    {
        await roleService.UpdateUserRoles(request.UserId, request.RoleIds, cancellationToken);
        return Success();
    }
}
