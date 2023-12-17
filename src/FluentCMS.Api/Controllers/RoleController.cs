using AutoMapper;
using FluentCMS.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

public class RoleController(IMapper mapper, IRoleService roleService) : BaseController
{
    [HttpGet]
    public async Task<IApiPagingResult<Role>> GetAll([FromQuery] Guid siteId, CancellationToken cancellationToken = default)
    {
        var roles = await roleService.GetAll(siteId, cancellationToken);
        return new ApiPagingResult<Role>(roles);
    }

    [HttpGet]
    public async Task<IApiResult<Role>> GetById([FromQuery] Guid id, [FromQuery] Guid siteId, CancellationToken cancellationToken = default)
    {
        var role = await roleService.GetById(id, siteId, cancellationToken);
        return new ApiResult<Role>(role);
    }

    [HttpPost]
    public async Task<IApiResult<Role>> Create([FromBody] RoleCreateRequest request, CancellationToken cancellationToken = default)
    {
        var role = mapper.Map<Role>(request);
        var newRole = await roleService.Create(role, cancellationToken);
        return new ApiResult<Role>(newRole);
    }

    [HttpPut]
    public async Task<IApiResult<Role>> Update([FromBody] RoleUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var role = mapper.Map<Role>(request);
        var editedRole = await roleService.Update(role, cancellationToken);
        return new ApiResult<Role>(editedRole);
    }

    [HttpDelete]
    public async Task<IApiResult<bool>> Delete([FromQuery] Guid id, [FromQuery] Guid siteId, CancellationToken cancellationToken = default)
    {
        await roleService.Delete(id, siteId, cancellationToken);
        return new ApiResult<bool>(true);
    }
}
