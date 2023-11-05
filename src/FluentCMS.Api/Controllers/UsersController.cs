using AutoMapper;
using FluentCMS.Api.Models;
using FluentCMS.Api.Models.Users;
using FluentCMS.Entities.Users;
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
        var newUser = await userService.Create(user);
        var result = mapper.Map<UserResponse>(newUser);
        return new ApiResult<UserResponse>(result);
    }

    [HttpPut]
    public async Task<IApiResult<UserResponse>> Edit(EditUserRequest request)
    {
        var user = mapper.Map<User>(request);
        var editedUser = await userService.Edit(user);
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
