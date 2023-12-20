using AutoMapper;
using FluentCMS.Web.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Web.Api.Controllers;

public class RoleController(IMapper mapper, IRoleService roleService) : BaseController
{
    [HttpGet("{siteId}")]
    public async Task<IApiPagingResult<Role>> GetAll([FromRoute] Guid siteId, CancellationToken cancellationToken = default)
    {
        var roles = await roleService.GetAll(siteId, cancellationToken);
        return new ApiPagingResult<Role>(roles);
    }

    [HttpGet("{id}")]
    public async Task<IApiResult<Role>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var role = await roleService.GetById(id, cancellationToken);
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

    [HttpDelete("{id}")]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        await roleService.Delete(id, cancellationToken);
        return new ApiResult<bool>(true);
    }
}
