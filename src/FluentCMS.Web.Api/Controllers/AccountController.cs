using FluentCMS.Providers;
using Microsoft.AspNetCore.Authorization;

namespace FluentCMS.Web.Api.Controllers;

public class AccountController(IMapper mapper, IUserService userService, IAuthContext authContext, ISmtpEmailProvider smtpEmailProvider) : BaseGlobalController
{

    [HttpPost]
    public async Task Send()
    {
        await smtpEmailProvider.Send("john.doe@example.org", "ap@momentaj.com", "subject", "body");
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IApiResult<UserDetailResponse>> Register([FromBody] UserRegisterRequest request, CancellationToken cancellationToken = default)
    {
        var user = mapper.Map<User>(request);
        var newUser = await userService.Create(user, request.Password, cancellationToken);
        var response = mapper.Map<UserDetailResponse>(newUser);
        return Ok(response);
    }

    [HttpPost]
    [AllowAnonymous]
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
    public async Task<IApiResult<bool>> ChangePassword([FromBody] AccountChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        await userService.ChangePassword(authContext.UserId, request.OldPassword, request.NewPassword, cancellationToken);
        return Ok(true);
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IApiResult<bool>> SendPasswordResetToken([FromBody] UserSendPasswordResetTokenRequest request, CancellationToken cancellationToken = default)
    {
        var token = await userService.GeneratePasswordResetToken(request.Email, cancellationToken);
        // todo send token to user's email address
        Console.WriteLine($"http://localhost:5000/auth/reset-password?email={request.Email}&token={token}");
        return Ok(true);
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IApiResult<bool>> ValidatePasswordResetToken([FromBody] UserValidatePasswordResetTokenRequest request, CancellationToken cancellationToken = default)
    {
        _ = await userService.ValidatePasswordResetToken(request.Token, request.Email, request.NewPassword, cancellationToken);
        return Ok(true);
    }

    [HttpGet]
    public async Task<IApiResult<UserDetailResponse>> GetCurrent(CancellationToken cancellationToken = default)
    {
        var user = await userService.GetById(authContext.UserId, cancellationToken);
        return Ok(mapper.Map<UserDetailResponse>(user));
    }

    [HttpPost]
    public async Task<IApiResult<UserDetailResponse>> UpdateCurrent(AccountUpdateRequest userUpdateRequest)
    {
        var user = await userService.GetById(authContext.UserId);
        mapper.Map(userUpdateRequest, user);
        await userService.Update(user);
        return Ok(mapper.Map<UserDetailResponse>(user));
    }
}
