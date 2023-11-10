using AutoMapper;
using FluentCMS.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

public class RolesController(IMapper mapper, IRoleService roleService) : BaseController
{
    [HttpGet]
    public async Task<IApiPagingResult<RoleDto>> GetAll([FromQuery] Guid siteId, CancellationToken cancellationToken = default)
    {
        var roles = await roleService.GetAll(siteId, cancellationToken);
        var result = mapper.Map<IEnumerable<RoleDto>>(roles);
        return new ApiPagingResult<RoleDto>(result);
    }

    [HttpGet("{id}")]
    public async Task<IApiResult<RoleDto>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var role = await roleService.GetById(id, cancellationToken);
        var result = mapper.Map<RoleDto>(role);
        return new ApiResult<RoleDto>(result);
    }

    [HttpPost]
    public async Task<IApiResult<RoleDto>> Create(RoleCreateRequest request, CancellationToken cancellationToken = default)
    {
        var role = mapper.Map<Role>(request);
        var newRole = await roleService.Create(role, cancellationToken);
        var result = mapper.Map<RoleDto>(newRole);
        return new ApiResult<RoleDto>(result);
    }

    [HttpPut]
    public async Task<IApiResult<RoleDto>> Update(RoleUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var role = mapper.Map<Role>(request);
        var editedRole = await roleService.Update(role, cancellationToken);
        var result = mapper.Map<RoleDto>(editedRole);
        return new ApiResult<RoleDto>(result);
    }

    [HttpDelete("{id}")]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var role = await roleService.GetById(id, cancellationToken);
        await roleService.Delete(role, cancellationToken);
        return new ApiResult<bool>(true);
    }
}
