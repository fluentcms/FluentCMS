﻿using FluentCMS.Web.Api.Models.Users;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Web.Api.Controllers;

public class AccountController(IMapper mapper, IUserService userService, ILogger<AccountController> logger) : BaseGlobalController
{
    [HttpPost]
    public async Task<IApiResult<UserDetailResponse>> Register([FromBody] UserRegisterRequest request, CancellationToken cancellationToken = default)
    {
        var user = mapper.Map<User>(request);
        var newUser = await userService.Create(user, request.Password, cancellationToken);
        var response = mapper.Map<UserDetailResponse>(newUser);
        return Ok(response);
    }

    [HttpPost]
    public async Task<IApiResult<UserLoginResponse>> Authenticate([FromBody] UserLoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userService.Authenticate(request.Username, request.Password, cancellationToken);
        var userToken = await userService.GetToken(user, cancellationToken);
        return Ok(new UserLoginResponse
        {
            Token = userToken.AccessToken,
            RoleIds = user.RoleIds,
            UserId = user.Id
        });
    }

    [HttpPost]
    public async Task<IApiResult<bool>> ChangePassword([FromBody] UserChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        await userService.ChangePassword(request.UserId, request.OldPassword, request.NewPassword, cancellationToken);
        return Ok(true);
    }

    [HttpPost]
    public async Task<IApiResult<bool>> SendPasswordResetToken([FromBody] UserSendPasswordResetTokenRequest request)
    {
        var token = await userService.GeneratePasswordResetToken(request.Email);
        logger.LogInformation("PasswordReset:{email}:{token}", request.Email, token);
        // todo send token 
        return Ok(true);
    }
    [HttpPost]
    public async Task<IApiResult<bool>> ValidatePasswordResetToken([FromBody] UserValidatePasswordResetTokenRequest request)
    {
        var result = await userService.ValidatePasswordResetToken(request.Token, request.Email, request.NewPassword);
        return Ok(true);
    }
}
