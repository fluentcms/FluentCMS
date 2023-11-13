using AutoMapper;
using FluentCMS.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

public class UserController(IMapper mapper, IUserService userService, IRoleService roleService) : BaseController
{
    private readonly IRoleService _roleService = roleService;

    [HttpGet]
    public async Task<IApiPagingResult<UserDto>> GetAll(CancellationToken cancellationToken = default)
    {
        var users = await userService.GetAll(cancellationToken);
        var result = mapper.Map<IEnumerable<UserDto>>(users);
        return new ApiPagingResult<UserDto>(result);
    }

    [HttpPost]
    public async Task<IApiResult<RoleDto>> Create(RoleCreateRequest request, CancellationToken cancellationToken = default)
    {
        var role = mapper.Map<Role>(request);
        var newRole = await _roleService.Create(role, cancellationToken);
        var result = mapper.Map<RoleDto>(newRole);
        return new ApiResult<RoleDto>(result);
    }

    [HttpGet("{id}")]
    public async Task<IApiResult<RoleDto>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var role = await _roleService.GetById(id, cancellationToken);
        var result = mapper.Map<RoleDto>(role);
        return new ApiResult<RoleDto>(result);
    }



    [HttpPut]
    public async Task<IApiResult<RoleDto>> Update(RoleUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var role = mapper.Map<Role>(request);
        var editedRole = await _roleService.Update(role, cancellationToken);
        var result = mapper.Map<RoleDto>(editedRole);
        return new ApiResult<RoleDto>(result);
    }

    [HttpDelete("{id}")]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var role = await _roleService.GetById(id, cancellationToken);
        await _roleService.Delete(role, cancellationToken);
        return new ApiResult<bool>(true);
    }
}
