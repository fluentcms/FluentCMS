namespace FluentCMS.Web.Api.Controllers;

public class AccountController(IMapper mapper, IAccountService accountService, IApiExecutionContext apiExecutionContext) : BaseGlobalController
{
    public const string AREA = "Account Management";
    public const string READ = "Read";
    public const string UPDATE = $"Update/{READ}";
    public const string REGISTER = "Register";
    public const string AUTHENTICATE = $"Authenticate";

    [HttpPost]
    [Policy(AREA, REGISTER)]
    public async Task<IApiResult<UserDetailResponse>> Register([FromBody] UserRegisterRequest request, CancellationToken cancellationToken = default)
    {
        var user = mapper.Map<User>(request);
        var newUser = await accountService.Register(user, request.Password, cancellationToken);
        var response = mapper.Map<UserDetailResponse>(newUser);
        return Ok(response);
    }

    [HttpPost]
    [Policy(AREA, AUTHENTICATE)]
    public async Task<IApiResult<UserLoginResponse>> Authenticate([FromBody] UserLoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await accountService.Authenticate(request.Username, request.Password, cancellationToken);
        var userToken = await accountService.GetToken(user, cancellationToken);
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
        await accountService.ChangePassword(apiExecutionContext.UserId, request.OldPassword, request.NewPassword, cancellationToken);
        return Ok(true);
    }

    [HttpPost]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<bool>> SendPasswordResetToken([FromBody] UserSendPasswordResetTokenRequest request, CancellationToken cancellationToken = default)
    {
        var result = await accountService.SendPasswordResetToken(request.Email, cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<bool>> ValidatePasswordResetToken([FromBody] UserValidatePasswordResetTokenRequest request, CancellationToken cancellationToken = default)
    {
        _ = await accountService.ChangePasswordByResetToken(request.Email, request.Token, request.NewPassword, cancellationToken);
        return Ok(true);
    }

    [HttpGet]
    [Policy(AREA, READ)]
    public async Task<IApiResult<UserDetailResponse>> GetCurrent(CancellationToken cancellationToken = default)
    {
        var user = await accountService.Get(cancellationToken);
        return Ok(mapper.Map<UserDetailResponse>(user));
    }

    [HttpPost]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<UserDetailResponse>> Update(AccountUpdateRequest userUpdateRequest, CancellationToken cancellationToken = default)
    {
        var user = await accountService.Get(cancellationToken);
        mapper.Map(userUpdateRequest, user);
        await accountService.Update(user, cancellationToken);
        return Ok(mapper.Map<UserDetailResponse>(user));
    }
}
