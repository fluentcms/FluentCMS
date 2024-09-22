using FluentCMS.Providers.EmailProviders;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
namespace FluentCMS.Services;

public interface IAccountService : IAutoRegisterService
{
    Task<User> Register(User user, string password, CancellationToken cancellationToken = default);
    Task<User> Get(CancellationToken cancellationToken = default);
    Task<User> Update(User user, CancellationToken cancellationToken = default);
    Task<User> Authenticate(string username, string password, CancellationToken cancellationToken = default);
    Task<User> ChangePassword(Guid id, string oldPassword, string newPassword, CancellationToken cancellationToken = default);
    Task<bool> ChangePasswordByResetToken(string email, string token, string newPassword, CancellationToken cancellationToken = default);
    Task<UserToken> GetToken(User user, CancellationToken cancellationToken = default);
    Task<bool> SendPasswordResetToken(string email, CancellationToken cancellationToken = default);
}

public class AccountService(UserManager<User> userManager, IEmailProvider emailProvider, IConfiguration configuration, IApiExecutionContext apiExecutionContext, IUserTokenProvider userTokenProvider, IMessagePublisher messagePublisher) : IAccountService
{
    public async Task<UserToken> GetToken(User user, CancellationToken cancellationToken = default)
    {
        // Generate token
        var userToken = await userTokenProvider.Generate(user);

        if (userToken is null || string.IsNullOrEmpty(userToken.AccessToken))
            throw new AppException(ExceptionCodes.UserTokenGenerationFailed);

        // Store refresh token
        var identityResult = await userManager.SetAuthenticationTokenAsync(user, ServiceConstants.LOCAL_LOGIN_PROVIDER, ServiceConstants.REFRESH_TOKEN_NAME, userToken.RefreshToken);

        identityResult.ThrowIfInvalid();

        return userToken;
    }

    public async Task<bool> SendPasswordResetToken(string email, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email) ??
            throw new AppException(ExceptionCodes.UserNotFound);

        var token = await userManager.GenerateUserTokenAsync(user, ServiceConstants.PASSWORD_RESET_TOKEN_PROVIDER, ServiceConstants.PASSWORD_RESET_PURPOSE);

        await emailProvider.Send(email, "Reset Password", $"{configuration["urls"]}/auth/reset-password?token={token}&email={email}", cancellationToken);

        await messagePublisher.Publish(new Message<User>(ActionNames.AccountPasswordSent, user), cancellationToken);

        return true;
    }

    public async Task<bool> ChangePasswordByResetToken(string email, string token, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email) ??
            throw new AppException(ExceptionCodes.UserNotFound);

        var result = await userManager.VerifyUserTokenAsync(user, ServiceConstants.PASSWORD_RESET_TOKEN_PROVIDER, ServiceConstants.PASSWORD_RESET_PURPOSE, token);
        if (result)
        {
            var resetResult = await userManager.ResetPasswordAsync(user, token, newPassword);
            resetResult.ThrowIfInvalid();

            // Update user properties related to password changing
            user.PasswordChangedAt = DateTime.Now;
            user.PasswordChangedBy = apiExecutionContext.Username;
            await userManager.UpdateAsync(user);

            await messagePublisher.Publish(new Message<User>(ActionNames.AccountPasswordChangeByToken, user), cancellationToken);
        }
        return result;
    }

    public async Task<User> ChangePassword(Guid id, string oldPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString()) ??
            throw new AppException(ExceptionCodes.UserNotFound);

        if (!await userManager.CheckPasswordAsync(user, oldPassword))
            throw new AppException(ExceptionCodes.UserChangePasswordFailed);

        var idResult = await userManager.ChangePasswordAsync(user, oldPassword, newPassword);

        idResult.ThrowIfInvalid();

        // Update user properties related to password changing
        user.PasswordChangedAt = DateTime.Now;
        user.PasswordChangedBy = apiExecutionContext.Username;
        await userManager.UpdateAsync(user);

        await messagePublisher.Publish(new Message<User>(ActionNames.AccountChangePassword, user), cancellationToken);

        return user;
    }

    public async Task<User> Authenticate(string username, string password, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByNameAsync(username);

        // Validate user password
        if (user is null || !user.Enabled || !await userManager.CheckPasswordAsync(user, password))
            throw new AppException(ExceptionCodes.UserLoginFailed);

        // Update user properties related to login
        user.LoginAt = DateTime.Now;
        user.LoginCount++;
        await userManager.UpdateAsync(user);

        await messagePublisher.Publish(new Message<User>(ActionNames.AccountAuthenticate, user), cancellationToken);

        return user;
    }

    public async Task<User> Register(User user, string password, CancellationToken cancellationToken = default)
    {
        var identityResult = await userManager.CreateAsync(user, password);
        identityResult.ThrowIfInvalid();

        await messagePublisher.Publish(new Message<User>(ActionNames.AccountRegister, user), cancellationToken);

        return user;
    }

    public async Task<User> Get(CancellationToken cancellationToken = default)
    {
        var userId = apiExecutionContext.UserId;

        var user = await userManager.FindByIdAsync(userId.ToString()) ??
            throw new AppException(ExceptionCodes.UserNotFound);

        return user;
    }

    public async Task<User> Update(User user, CancellationToken cancellationToken = default)
    {
        if (user.Id != apiExecutionContext.UserId)
            throw new AppException(ExceptionCodes.UserNotFound);

        var result = await userManager.UpdateAsync(user);
        result.ThrowIfInvalid();

        await messagePublisher.Publish(new Message<User>(ActionNames.AccountUpdated, user), cancellationToken);

        return user;
    }
}
