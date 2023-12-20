using AutoMapper;
using FluentCMS.Web.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Web.Api.Controllers;

public class AccountController(IMapper mapper, IUserService userService) : BaseController
{
    [HttpPost]
    public async Task<IApiResult<UserDetailResponse>> Register(UserRegisterRequest request, CancellationToken cancellationToken = default)
    {
        var user = mapper.Map<User>(request);
        var newUser = await userService.Create(user, request.Password, cancellationToken);
        var result = mapper.Map<UserDetailResponse>(newUser);
        return new ApiResult<UserDetailResponse>(result);
    }

    [HttpPost]
    public async Task<IApiResult<UserLoginResponse>> Login(UserLoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userService.Login(request.Username, request.Password, cancellationToken);
        var userToken = await userService.GetToken(user, cancellationToken);
        return new ApiResult<UserLoginResponse>(new UserLoginResponse
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
