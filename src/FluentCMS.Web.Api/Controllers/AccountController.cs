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
    public async Task<IApiResult<bool>> SendPasswordResetToken([FromBody] UserSendPasswordResetTokenRequest request)
    {
        var token = await userService.GeneratePasswordResetToken(request.Email);
        // todo send token 
        return Ok(true);
    }

    [HttpPost]
    public async Task<IApiResult<bool>> ValidatePasswordResetToken([FromBody] UserValidatePasswordResetTokenRequest request)
    {
        _ = await userService.ValidatePasswordResetToken(request.Token, request.Email, request.NewPassword);
        return Ok(true);
    }

    [HttpGet]
    [Authorize]
    public async Task<IApiResult<UserDetailResponse>> GetCurrent()
    {
        var user = await userService.GetById(authContext.UserId);
        return Ok(mapper.Map<UserDetailResponse>(user));
    }
    [HttpPost]
    [Authorize]
    public async Task<IApiResult<UserDetailResponse>> UpdateCurrent(AccountUpdateRequest userUpdateRequest)
    {
        var user = await userService.GetById(authContext.UserId);
        user.FirstName = userUpdateRequest.FirstName;
        user.LastName = userUpdateRequest.LastName;
        user.Email = userUpdateRequest.Email;
        user.PhoneNumber = userUpdateRequest.PhoneNumber;
        await userService.Update(user);
        return Ok(mapper.Map<UserDetailResponse>(user));
    }
}
