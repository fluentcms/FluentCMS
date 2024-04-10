using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace FluentCMS.Web.Api.Controllers;

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class RoleController(IMapper mapper, IRoleService roleService) : BaseGlobalController
{
    [HttpGet]
    public async Task<IApiPagingResult<RoleDetailResponse>> GetAll(CancellationToken cancellationToken = default)
    {
        var roles = await roleService.GetAll(cancellationToken);
        var roleResponses = mapper.Map<IEnumerable<RoleDetailResponse>>(roles);
        return OkPaged(roleResponses);
    }

    [HttpPost]
    public async Task<IApiResult<RoleDetailResponse>> Create([FromBody] RoleCreateRequest request, CancellationToken cancellationToken = default)
    {
        var role = mapper.Map<Role>(request);
        var newRole = await roleService.Create(role, cancellationToken);
        var roleResponse = mapper.Map<RoleDetailResponse>(newRole);
        return Ok(roleResponse);
    }

    [HttpPut]
    public async Task<IApiResult<RoleDetailResponse>> Update([FromBody] RoleUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var role = mapper.Map<Role>(request);
        var updated = await roleService.Update(role, cancellationToken);
        var roleResponse = mapper.Map<RoleDetailResponse>(updated);
        return Ok(roleResponse);
    }

    [HttpDelete("{id}")]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        await roleService.Delete(id, cancellationToken);
        return Ok(true);
    }
}
