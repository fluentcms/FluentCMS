using AutoMapper;
using FluentCMS.Api.Models;
using FluentCMS.Api.Models.Users;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;
public class UsersController(IMapper mapper, IUserService userService) : BaseController
{
    [HttpGet]
    public async Task<IApiPagingResult<UserResponse>> GetAll([FromQuery] SearchUserRequest request)
    {
        var users = await userService.GetAll();
        var result = mapper.Map<IEnumerable<UserResponse>>(users);
        return new ApiPagingResult<UserResponse>(result);
    }

    [HttpGet("{id}")]
    public async Task<IApiResult<UserResponse>> GetById([FromRoute] Guid id)
    {
        var user = await userService.GetById(id);
        var result = mapper.Map<UserResponse>(user);
        return new ApiResult<UserResponse>(result);
    }

    [HttpPost]
    public async Task<IApiResult<UserResponse>> Create(CreateUserRequest request)
    {
        var user = mapper.Map<User>(request);
        user.Id = Guid.NewGuid();
        user.UserRoles = request.Roles?.Select(x => new UserRole
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RoleId = x
        }).ToList() ?? [];
        var newUser = await userService.Create(user);
        var result = mapper.Map<UserResponse>(newUser);
        return new ApiResult<UserResponse>(result);
    }

    [HttpPut]
    public async Task<IApiResult<UserResponse>> Update(EditUserRequest request)
    {
        var user = mapper.Map<User>(request);
        user.UserRoles = request.Roles?.Select(x => new UserRole
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RoleId = x
        }).ToList() ?? [];
        var editedUser = await userService.Update(user);
        var result = mapper.Map<UserResponse>(editedUser);
        return new ApiResult<UserResponse>(result);
    }

    [HttpDelete("{id}")]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id)
    {
        var user = await userService.GetById(id);
        await userService.Delete(user);
        return new ApiResult<bool>(true);
    }
}
