using AutoMapper;
using FluentCMS.Api.Models;
using FluentCMS.Api.Models.Users;
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
        var user = await userService.Create(
            name: request.Name,
            username: request.Username,
            password: request.Password,
            roles: request.Roles);
        return new ApiResult<UserResponse>(
            mapper.Map<UserResponse>(user));
    }

    [HttpPut]
    public async Task<IApiResult<UserResponse>> Edit(EditUserRequest request)
    {
        var user = await userService.Edit(request.Id,
            name: request.Name,
            username: request.Username,
            password: request.Password,
            roles: request.Roles);
        return new ApiResult<UserResponse>(
            mapper.Map<UserResponse>(user));
    }

    [HttpDelete("{id}")]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id)
    {
        await userService.Delete(id);
        return new ApiResult<bool>(true);
    }
}
