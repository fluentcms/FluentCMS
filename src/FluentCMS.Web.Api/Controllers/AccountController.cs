using FluentCMS.Web.Api.Models.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace FluentCMS.Web.Api.Controllers;

public class AccountController(IMapper mapper, IUserService userService, ILogger<AccountController> logger, IHttpContextAccessor httpContextAccessor) : BaseGlobalController
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
    [Authorize]
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

    [HttpGet]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IApiResult<UserDetailResponse>> GetUserDetail()
    {
        var userId = Guid.Parse(httpContextAccessor!.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier));
        var user = await userService.GetById(userId);
        return Ok(mapper.Map<UserDetailResponse>(user));
    }
    [HttpGet]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IApiResult<UserDetailResponse>> SetUserDetail(UserUpdateProfileRequest request, CancellationToken cancellationToken = default)
    {
        var userId = Guid.Parse(httpContextAccessor!.HttpContext!.User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = mapper.Map<User>(request);
        user.Id = userId;
        var updated = await userService.Update(user, cancellationToken);
        var userResponse = mapper.Map<UserDetailResponse>(updated);
        return Ok(userResponse);
    }
}
