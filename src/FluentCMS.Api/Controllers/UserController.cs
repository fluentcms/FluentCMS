using AutoMapper;
using FluentCMS.Api.Models;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

public class UserController(IMapper mapper, IUserService userService, IRoleService roleService) : BaseController
{
    private readonly IRoleService _roleService = roleService;

    [HttpGet]
    public async Task<IApiPagingResult<UserDto>> GetAll(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    [HttpPost]
    public async Task<IApiResult<RoleDto>> Create(RoleCreateRequest request, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    [HttpGet("{id}")]
    public async Task<IApiResult<RoleDto>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    [HttpPut]
    public async Task<IApiResult<RoleDto>> Update(RoleUpdateRequest request, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    [HttpDelete("{id}")]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
