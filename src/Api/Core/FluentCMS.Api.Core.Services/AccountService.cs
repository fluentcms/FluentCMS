namespace FluentCMS.Api.Core.Services;

public interface IAccountService
{
    Task<User> Register(User user, string password, CancellationToken cancellationToken = default);
    Task<User> Get(CancellationToken cancellationToken = default);
    Task<User> Update(User user, CancellationToken cancellationToken = default);
    Task<User> Authenticate(string username, string password, CancellationToken cancellationToken = default);
    Task<User> ChangePassword(Guid id, string oldPassword, string newPassword, CancellationToken cancellationToken = default);
    Task<bool> ChangePasswordByResetToken(string email, string token, string newPassword, CancellationToken cancellationToken = default);
    Task<bool> SendResetPasswordToken(string email, CancellationToken cancellationToken = default);
    Task<bool> ValidatePassword(string password, CancellationToken cancellationToken = default);
    Task<bool> ValidateUserName(string username, CancellationToken cancellationToken = default);
}

public class AccountService(UserManager<User> userManager, IEmailProvider emailProvider, IConfiguration configuration, IApplicationExecutionContext executionContext, IEventPublisher eventPublisher) : IAccountService
{

    public async Task<bool> SendResetPasswordToken(string email, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email) ??
            throw new EnhancedException(ExceptionCodes.UserNotFound);

        var token = await userManager.GenerateUserTokenAsync(user, ServiceConstants.PASSWORD_RESET_TOKEN_PROVIDER, ServiceConstants.PASSWORD_RESET_PURPOSE);

        await emailProvider.Send(email, "Reset Password", $"{configuration["urls"]}/auth/reset-password?token={token}&email={email}", cancellationToken);

        await eventPublisher.Publish(new Message<User>(ActionNames.AccountPasswordSent, user), cancellationToken);

        return true;
    }

    public async Task<bool> ChangePasswordByResetToken(string email, string token, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email) ??
            throw new EnhancedException(ExceptionCodes.UserNotFound);

        var result = await userManager.VerifyUserTokenAsync(user, ServiceConstants.PASSWORD_RESET_TOKEN_PROVIDER, ServiceConstants.PASSWORD_RESET_PURPOSE, token);
        if (result)
        {
            var resetResult = await userManager.ResetPasswordAsync(user, token, newPassword);
            resetResult.ThrowIfInvalid();

            // Update user properties related to password changing
            user.PasswordChangedAt = DateTime.Now;
            user.PasswordChangedBy = executionContext.Username;
            await userManager.UpdateAsync(user);

            await eventPublisher.Publish(new AccountChangedPasswordByResetTokenEvent(user), cancellationToken);
        }
        return result;
    }

    public async Task<User> ChangePassword(Guid id, string oldPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString()) ??
            throw new EnhancedException(ExceptionCodes.UserNotFound);

        if (!await userManager.CheckPasswordAsync(user, oldPassword))
            throw new EnhancedException(ExceptionCodes.UserChangePasswordFailed);

        var idResult = await userManager.ChangePasswordAsync(user, oldPassword, newPassword);

        idResult.ThrowIfInvalid();

        // Update user properties related to password changing
        user.PasswordChangedAt = DateTime.Now;
        user.PasswordChangedBy = executionContext.Username;
        var identityResult = await userManager.UpdateAsync(user);
        identityResult.ThrowIfInvalid();

        await eventPublisher.Publish(new AccountChangedPasswordEvent(user), cancellationToken);

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

    public async Task<User> Register(User user, string password, CancellationToken cancellationToken = default)
    {
        var identityResult = await userManager.CreateAsync(user, password);
        identityResult.ThrowIfInvalid();

        await eventPublisher.Publish(new AccountRegisteredEvent(user), cancellationToken);

        return user;
    }

    public async Task<User> Get(CancellationToken cancellationToken = default)
    {
        var userId = executionContext.UserId;

        if (userId == null || userId == Guid.Empty)
            throw new EnhancedException(ExceptionCodes.UserNotFound);

        var user = await userManager.FindByIdAsync(userId.ToString()!) ??
            throw new EnhancedException(ExceptionCodes.UserNotFound);

        return user;
    }

    public async Task<User> Update(User user, CancellationToken cancellationToken = default)
    {
        if (user.Id != executionContext.UserId)
            throw new EnhancedException(ExceptionCodes.UserNotFound);

        var result = await userManager.UpdateAsync(user);
        result.ThrowIfInvalid();

        await eventPublisher.Publish(new AccountUpdatedEvent(user), cancellationToken);

        return user;
    }

    public async Task<bool> ValidatePassword(string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new EnhancedException(ExceptionCodes.UserInvalidPassword);

        var passwordValidators = userManager.PasswordValidators;
        var identityUser = new User(); // Create a dummy user for validation.

        foreach (var validator in passwordValidators)
        {
            var result = await validator.ValidateAsync(userManager, identityUser, password);
            result.ThrowIfInvalid();
        }

        return true;
    }

    public Task<bool> ValidateUserName(string username, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new EnhancedException(ExceptionCodes.UserInvalidUsername);

        if (username.Length < 3 || username.Length > 50)
            throw new EnhancedException(ExceptionCodes.UserInvalidUsername);

        return Task.FromResult(true);
    }

}

