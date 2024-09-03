using Microsoft.AspNetCore.Authorization;

namespace FluentCMS.Web.Api.Controllers;

public class AccountController(IMapper mapper, IUserService userService, IApiExecutionContext apiExecutionContext) : BaseGlobalController
{
    public const string AREA = "Account Management";
    public const string READ = "Read";
    public const string UPDATE = $"Update/{READ}";
    public const string CREATE = "Create";
    public const string DELETE = $"Delete/{READ}";
    public const string AUTHENTICATE = $"Authenticate";

    [HttpPost]
    [Policy(AREA, CREATE)]
    public async Task<IApiResult<UserDetailResponse>> Register([FromBody] UserRegisterRequest request, CancellationToken cancellationToken = default)
    {
        var user = mapper.Map<User>(request);
        var newUser = await userService.Create(user, request.Password, cancellationToken);
        var response = mapper.Map<UserDetailResponse>(newUser);
        return Ok(response);
    }

    [HttpPost]
    [Policy(AREA, AUTHENTICATE)]
    [AllowAnonymous]
    public async Task<IApiResult<UserLoginResponse>> Authenticate([FromBody] UserLoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userService.Authenticate(request.Username, request.Password, cancellationToken);
        var userToken = await userService.GetToken(user, cancellationToken);
        return Ok(new UserLoginResponse
        {
            Token = userToken.AccessToken,
            UserId = user.Id,
            UserName = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty
        });
    }

    [HttpPost]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<bool>> ChangePassword([FromBody] AccountChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        await userService.ChangePassword(apiExecutionContext.UserId, request.OldPassword, request.NewPassword, cancellationToken);
        return Ok(true);
    }

    [HttpPost]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<bool>> SendPasswordResetToken([FromBody] UserSendPasswordResetTokenRequest request, CancellationToken cancellationToken = default)
    {
        var result = await userService.SendPasswordResetToken(request.Email, cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<bool>> ValidatePasswordResetToken([FromBody] UserValidatePasswordResetTokenRequest request, CancellationToken cancellationToken = default)
    {
        _ = await userService.ChangePasswordByResetToken(request.Email, request.Token, request.NewPassword, cancellationToken);
        return Ok(true);
    }

    [HttpGet]
    [Policy(AREA, READ)]
    public async Task<IApiResult<UserDetailResponse>> GetCurrent(CancellationToken cancellationToken = default)
    {
        var user = await userService.GetById(apiExecutionContext.UserId, cancellationToken);
        return Ok(mapper.Map<UserDetailResponse>(user));
    }

    [HttpPost]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<UserDetailResponse>> UpdateCurrent(AccountUpdateRequest userUpdateRequest)
    {
        var user = await userService.GetById(apiExecutionContext.UserId);
        mapper.Map(userUpdateRequest, user);
        await userService.Update(user);
        return Ok(mapper.Map<UserDetailResponse>(user));
    }
}
