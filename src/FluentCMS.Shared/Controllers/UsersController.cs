using FluentCMS.Application.Dtos.Users;
using FluentCMS.Application.Services;
using FluentCMS.Models;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Web.Controllers;
public class UsersController(IUserService userService) : BaseController
{
    [HttpGet]
    public async Task<ActionResult<ApiResult<SearchUserResponse>>> Search([FromQuery] SearchUserRequest request)
    {
        var data = await userService.Search(request);
        return SuccessResult(data);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResult<UserDto>>> GetById([FromRoute] Guid id)
    {
        var data = await userService.GetById(id);
        return SuccessResult(data);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResult<Guid>>> Create(CreateUserRequest request)
    {
        var result = await userService.Create(request);
        return SuccessResult(result);
    }

    [HttpPut]
    public async Task<ActionResult<ApiResult<bool>>> Edit(EditUserRequest request)
    {
        await userService.Edit(request);
        return SuccessResult(true);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResult<bool>>> Delete([FromRoute] DeleteUserRequest request)
    {
        await userService.Delete(request);
        return SuccessResult(true);
    }
}
