namespace FluentCMS.Api.Plugins.IdentityManagement.Controllers;

public class AccountsController(IAccountService accountService) : BaseController
{
    [HttpPost]
    public async Task<ApiResponse> Register(AccountRegisterRequest request, CancellationToken cancellationToken = default)
    {
        await accountService.Register(request.UserName, request.Email, request.Password, cancellationToken);
        return Success(MessageCodes.AccountRegisteredSuccessfully);
    }

    [HttpPost]
    public async Task<ApiResponse<LoginDto>> Login(AccountLoginRequest request, CancellationToken cancellationToken = default)
    {
        var token = await accountService.Login(request.UserName, request.Password, cancellationToken);
        return Success(new LoginDto { Token = token }, MessageCodes.AccountLoginSuccessful);
    }

    [HttpGet]
    public async Task<ApiResponse> Logout(CancellationToken cancellationToken = default)
    {
        await accountService.Logout(cancellationToken);
        return Success(MessageCodes.AccountLogoutSuccessful);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse>> ConfirmEmail(AccountConfirmEmailRequest confirmEmailRequest, CancellationToken cancellationToken = default)
    {
        await accountService.ConfirmEmail(confirmEmailRequest.UserName, confirmEmailRequest.Email, confirmEmailRequest.Token, cancellationToken);
        return Success(MessageCodes.AccountEmailConfirmedSuccessfully);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse>> ResendConfirmation(AccountResendConfirmationRequest resendConfirmationRequest, CancellationToken cancellationToken = default!)
    {
        await accountService.ResendConfirmation(resendConfirmationRequest.Email, cancellationToken);
        return Success(MessageCodes.AccountConfirmationEmailSent);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse>> ForgotPassword(AccountForgotPasswordRequest forgotPasswordRequest, CancellationToken cancellationToken = default!)
    {
        await accountService.ForgotPassword(forgotPasswordRequest.Email, cancellationToken);
        return Success(MessageCodes.AccountPasswordResetEmailSent);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse>> ResetPassword(AccountResetPasswordRequest resetPasswordRequest, CancellationToken cancellationToken = default!)
    {
        await accountService.ResetPassword(resetPasswordRequest.Email, resetPasswordRequest.Token, resetPasswordRequest.NewPassword, cancellationToken);
        return Success(MessageCodes.AccountPasswordResetSuccessfully);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse>> ChangePassword(AccountChangePasswordRequest changePasswordRequest, CancellationToken cancellationToken = default!)
    {
        await accountService.ChangePassword(changePasswordRequest.UserName, changePasswordRequest.OldPassword, changePasswordRequest.NewPassword, cancellationToken);
        return Success(MessageCodes.AccountPasswordChangedSuccessfully);
    }
}
