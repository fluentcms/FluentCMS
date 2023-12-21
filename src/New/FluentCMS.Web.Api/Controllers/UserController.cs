using AutoMapper;
using FluentCMS.Entities;
using FluentCMS.Services;
using FluentCMS.Web.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Web.Api.Controllers;

public class UserController(IUserService userService, IMapper mapper) : BaseSystemController
{

    [HttpGet]
    public async Task<IApiPagingResult<UserResponse>> GetAll(CancellationToken cancellationToken = default)
    {
        var users = await userService.GetAll(cancellationToken);
        var usersResponse = mapper.Map<List<UserResponse>>(users);
        return OkPaged(usersResponse);
    }

    [HttpGet("{userId}")]
    public async Task<IApiResult<UserResponse>> Get([FromRoute] Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await userService.GetById(userId, cancellationToken);
        var userResponse = mapper.Map<UserResponse>(user);
        return Ok(userResponse);
    }

    [HttpPut]
    public async Task<IApiResult<UserResponse>> Update([FromBody] UserUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var user = mapper.Map<User>(request);
        var updated = await userService.Update(user, cancellationToken);
        var userResponse = mapper.Map<UserResponse>(updated);
        return Ok(userResponse);
    }

    [HttpPost]
    public async Task<IApiResult<UserResponse>> Create([FromBody] UserCreateRequest request, CancellationToken cancellationToken = default)
    {
        var user = mapper.Map<User>(request);
        var created = await userService.Create(user, request.Password, cancellationToken);
        var userResponse = mapper.Map<UserResponse>(created);
        return Ok(userResponse);
    }
}
