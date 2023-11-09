using AutoMapper;
using FluentCMS.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;
public class RolesController(IMapper mapper, IRoleService roleService) : BaseController
{
    [HttpGet]
    public async Task<IApiPagingResult<RoleResponse>> GetAll([FromQuery] RoleSearchRequest request)
    {
        var roles = await roleService.GetAll();
        var result = mapper.Map<IEnumerable<RoleResponse>>(roles);
        return new ApiPagingResult<RoleResponse>(result);
    }

    [HttpGet("{id}")]
    public async Task<IApiResult<RoleResponse>> GetById([FromRoute] Guid id)
    {
        var role = await roleService.GetById(id);
        var result = mapper.Map<RoleResponse>(role);
        return new ApiResult<RoleResponse>(result);
    }

    [HttpPost]
    public async Task<IApiResult<RoleResponse>> Create(RoleCreateRequest request)
    {
        var role = mapper.Map<Role>(request);
        var newRole = await roleService.Create(role);
        var result = mapper.Map<RoleResponse>(newRole);
        return new ApiResult<RoleResponse>(result);
    }

    [HttpPut]
    public async Task<IApiResult<RoleResponse>> Update(RoleUpdateRequest request)
    {
        var role = mapper.Map<Role>(request);
        var updatedRole = await roleService.Update(role);
        var result = mapper.Map<RoleResponse>(updatedRole);
        return new ApiResult<RoleResponse>(result);
    }

    [HttpDelete("{id}")]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id)
    {
        var role = await roleService.GetById(id);
        await roleService.Delete(role);
        return new ApiResult<bool>(true);
    }
}
