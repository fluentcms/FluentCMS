namespace FluentCMS.Plugins.IdentityManager.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public class RolesController(IRoleService roleService, IMapper mapper)
{
    [HttpGet]
    public async Task<ApiPagedResult<RoleResponse>> GetAll(CancellationToken cancellationToken = default)
    {
        var roles = await roleService.GetAll(cancellationToken);
        return mapper.ToPagedApiResult<RoleResponse>(roles);
    }

    [HttpGet("{id:guid}")]
    public async Task<ApiResult<RoleResponse>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var role = await roleService.GetById(id, cancellationToken);
        return mapper.ToApiResult<RoleResponse>(role);
    }

    [HttpPost]
    public async Task<ApiResult<RoleResponse>> Add([FromBody] RoleRequest request, CancellationToken cancellationToken = default)
    {
        var role = mapper.Map<Role>(request);
        role.Type = RoleTypes.UserDefined;
        await roleService.Add(role, cancellationToken);
        return mapper.ToApiResult<RoleResponse>(role);
    }

    [HttpPut("{id:guid}")]
    public async Task<ApiResult<RoleResponse>> Update(Guid id, [FromBody] RoleRequest request, CancellationToken cancellationToken = default)
    {
        var role = await roleService.GetById(id, cancellationToken);
        if (role.Type != RoleTypes.UserDefined)
            throw new EnhancedException("Role.CanNotUpdate", "Cannot update a built-in role.");

        mapper.Map<Role>(request);

        await roleService.Update(role, cancellationToken);

        return mapper.ToApiResult<RoleResponse>(role);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ApiResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var role = await roleService.GetById(id, cancellationToken);

        if (role.Type != RoleTypes.UserDefined)
            throw new EnhancedException("Role.CanNotDelete", "Cannot delete a built-in role.");

        await roleService.Remove(id, cancellationToken);
        return new ApiResult();
    }

}