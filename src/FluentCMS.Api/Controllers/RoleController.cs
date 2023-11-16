using AutoMapper;
using FluentCMS.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

public class RoleController(IMapper mapper, IRoleService roleService) : BaseController
{
    [HttpGet]
    public async Task<RolesResponse> GetAll(SiteIdRequest request, CancellationToken cancellationToken = default)
    {
        var roles = await roleService.GetAll(request.SiteId, cancellationToken);
        var result = mapper.Map<IEnumerable<RoleDto>>(roles);
        return new RolesResponse(result);
    }

    [HttpGet]
    public async Task<RoleResponse> GetById(IdRequest request, CancellationToken cancellationToken = default)
    {
        var role = await roleService.GetById(request.SiteId, request.Id, cancellationToken);
        var result = mapper.Map<RoleDto>(role);
        return new RoleResponse(result);
    }

    [HttpPost]
    public async Task<RoleResponse> Create(RoleCreateRequest request, CancellationToken cancellationToken = default)
    {
        var role = mapper.Map<Role>(request);
        var newRole = await roleService.Create(role, cancellationToken);
        var result = mapper.Map<RoleDto>(newRole);
        return new RoleResponse(result);
    }

    [HttpPut]
    public async Task<RoleResponse> Update(RoleUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var role = mapper.Map<Role>(request);
        var editedRole = await roleService.Update(role, cancellationToken);
        var result = mapper.Map<RoleDto>(editedRole);
        return new RoleResponse(result);
    }

    [HttpDelete]
    public async Task<BooleanResponse> Delete(IdRequest request, CancellationToken cancellationToken = default)
    {
        var role = await roleService.GetById(request.SiteId, request.Id, cancellationToken);
        await roleService.Delete(role, cancellationToken);
        return new BooleanResponse(true);
    }
}
