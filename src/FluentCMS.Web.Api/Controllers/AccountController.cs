using FluentCMS.Web.Api.Models.Users;
using Microsoft.AspNetCore.Authorization;

namespace FluentCMS.Web.Api.Controllers;

public class AccountController(IMapper mapper, IUserService userService, IAuthContext authContext) : BaseGlobalController
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
            UserId = user.Id,
            UserName = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty
        });
    }

    [HttpPost]
    [Authorize]
    public async Task<IApiResult<bool>> ChangePassword([FromBody] UserChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        await userService.ChangePassword(request.UserId, request.OldPassword, request.NewPassword, cancellationToken);
        return Ok(true);
    }

    [HttpPost]
    public async Task<IApiResult<bool>> SendPasswordResetToken([FromBody] UserSendPasswordResetTokenRequest request, CancellationToken cancellationToken = default)
    {
        var token = await userService.GeneratePasswordResetToken(request.Email, cancellationToken);
        // todo send token 
        return Ok(true);
    }

    [HttpPost]
    public async Task<IApiResult<bool>> ValidatePasswordResetToken([FromBody] UserValidatePasswordResetTokenRequest request, CancellationToken cancellationToken = default)
    {
        _ = await userService.ValidatePasswordResetToken(request.Token, request.Email, request.NewPassword, cancellationToken);
        return Ok(true);
    }

    [HttpGet]
    [Authorize]
    public async Task<IApiResult<UserDetailResponse>> GetUserDetail(CancellationToken cancellationToken = default)
    {
        var user = await userService.GetById(authContext.UserId, cancellationToken);
        return Ok(mapper.Map<UserDetailResponse>(user));
    }
}
