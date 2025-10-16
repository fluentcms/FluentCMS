namespace FluentCMS.Api.Plugins.IdentityManagement.Services;

public interface IAccountService
{
    Task<User> Register(User user, string password, CancellationToken cancellationToken = default);
    Task<User> Authenticate(string username, string password, CancellationToken cancellationToken = default);
    Task<User> ChangePassword(Guid id, string oldPassword, string newPassword, CancellationToken cancellationToken = default);
    Task<bool> ChangePasswordByResetToken(string email, string token, string newPassword, CancellationToken cancellationToken = default);
    Task<bool> SendResetPasswordToken(string email, CancellationToken cancellationToken = default);
}

internal class AccountService(UserManager<User> userManager, IEmailProvider emailProvider, IConfiguration configuration, ISecurityContext securityContext, IEventPublisher eventPublisher) : IAccountService
{
    public const string PASSWORD_RESET_PURPOSE = "passwordReset";
    public const string PASSWORD_RESET_TOKEN_PROVIDER = "passwordResetProvider";

    public async Task<User> Register(User user, string password, CancellationToken cancellationToken = default)
    {
        var identityResult = await userManager.CreateAsync(user, password);
        identityResult.ThrowIfInvalid();

        await eventPublisher.Publish(new AccountRegisteredEvent(user), cancellationToken);

        return user;
    }

    public async Task<User> Authenticate(string username, string password, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByNameAsync(username);

        // Validate user password
        if (user is null || !user.Enabled || !await userManager.CheckPasswordAsync(user, password))
            throw new EnhancedException(ExceptionCodes.UserLoginFailed);

        // Update user properties related to login
        user.LastLogin = DateTime.Now;
        user.LoginCount++;
        var identityResult = await userManager.UpdateAsync(user);
        identityResult.ThrowIfInvalid();

        await eventPublisher.Publish(new AccountAuthenticatedEvent(user), cancellationToken);

        return user;
    }

    public async Task<User> ChangePassword(Guid id, string oldPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString()) ??
            throw new EntityNotFoundException<User>(id);

        if (!await userManager.CheckPasswordAsync(user, oldPassword))
            throw new EnhancedException(ExceptionCodes.UserChangePasswordFailed);

        var idResult = await userManager.ChangePasswordAsync(user, oldPassword, newPassword);

        idResult.ThrowIfInvalid();

        // Update user properties related to password changing
        user.PasswordChangedAt = DateTime.Now;
        user.PasswordChangedBy = securityContext.Username;
        var identityResult = await userManager.UpdateAsync(user);
        identityResult.ThrowIfInvalid();

        await eventPublisher.Publish(new AccountChangedPasswordEvent(user), cancellationToken);

        return user;
    }

    public async Task<bool> ChangePasswordByResetToken(string email, string token, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email) ??
            throw new EntityNotFoundException<User>();

        var result = await userManager.VerifyUserTokenAsync(user, PASSWORD_RESET_TOKEN_PROVIDER, PASSWORD_RESET_PURPOSE, token);
        if (result)
        {
            var resetResult = await userManager.ResetPasswordAsync(user, token, newPassword);
            resetResult.ThrowIfInvalid();

            // Update user properties related to password changing
            user.PasswordChangedAt = DateTime.Now;
            user.PasswordChangedBy = securityContext.Username;
            await userManager.UpdateAsync(user);

            await eventPublisher.Publish(new AccountChangedPasswordByResetTokenEvent(user), cancellationToken);
        }
        return result;
    }

    public async Task<bool> SendResetPasswordToken(string email, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email) ??
            throw new EntityNotFoundException<User>();

        var token = await userManager.GenerateUserTokenAsync(user, PASSWORD_RESET_TOKEN_PROVIDER, PASSWORD_RESET_PURPOSE);

        // TODO: Use a proper email template
        // TODO: Auto-detect frontend URL based on the request context
        await emailProvider.Send(email, "Reset Password", $"{configuration["urls"]}/auth/reset-password?token={token}&email={email}", null, cancellationToken);

        await eventPublisher.Publish(new AccountSendResetPasswordTokenEvent(user), cancellationToken);

        return true;
    }
}

