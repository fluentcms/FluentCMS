using AutoMapper;
using FluentCMS.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Services;
using FluentCMS.Services.Models;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

public class AccountController(IMapper mapper, IUserService userService) : BaseController
{
    [HttpPost]
    public async Task<IApiResult<UserDto>> Register(UserRegisterRequest request, CancellationToken cancellationToken = default)
    {
        var user = mapper.Map<User>(request);
        var newUser = await userService.Create(user, request.Password, cancellationToken);
        var result = mapper.Map<UserDto>(newUser);
        return new ApiResult<UserDto>(result);
    }

    [HttpPost]
    public async Task<IApiResult<UserAuthenticateDto>> Authenticate(UserAuthenticateRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userService.Authenticate(request.Username, request.Password, cancellationToken);
        var userToken = await userService.GetToken(user, false, cancellationToken);
        return new ApiResult<UserAuthenticateDto>(new UserAuthenticateDto
        {
            Token = userToken.AccessToken,
            RoleIds = user.RoleIds,
            UserId = user.Id
        });
    }

    [HttpPost]
    public async Task<IApiResult<bool>> ChangePassword(UserChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        await userService.ChangePassword(request.UserId, request.OldPassword, request.NewPassword, cancellationToken);
        return new ApiResult<bool>(true);
    }

}
