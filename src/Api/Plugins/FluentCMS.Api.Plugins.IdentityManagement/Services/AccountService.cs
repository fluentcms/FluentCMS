namespace FluentCMS.Api.Plugins.IdentityManagement.Services;

public interface IAccountService
{
    Task Register(string username, string email, string password, CancellationToken cancellationToken = default);
    Task<string> Login(string username, string password, CancellationToken cancellationToken = default);
    Task Logout(CancellationToken cancellationToken = default);
    Task ConfirmEmail(string username, string email, string emailToken, CancellationToken cancellationToken = default);
    Task ResendConfirmation(string email, CancellationToken cancellationToken = default);
    Task ForgotPassword(string email, CancellationToken cancellationToken = default);
    Task ResetPassword(string email, string token, string newPassword, CancellationToken cancellationToken = default);
    Task ChangePassword(string username, string oldPassword, string newPassword, CancellationToken cancellationToken = default);
}

internal class AccountService(UserManager<User> userManager, ISecurityContext securityContext, Logger<AccountService> logger, IEmailSender emailSender, SignInManager<User> signInManager, ITokenGenerator tokenGenerator) : IAccountService
{
    public async Task Register(string username, string email, string password, CancellationToken cancellationToken = default)
    {
        // Check if user already exists
        var existingUser = await userManager.FindByEmailAsync(email);
        if (existingUser != null)
            throw new EnhancedException(MessageCodes.AccountEmailAlreadyExists);

        var user = new User
        {
            UserName = username,
            Email = email,
            EmailConfirmed = false,
            IsSuperAdmin = false,
            Suspended = false
        };

        var identityResult = await userManager.CreateAsync(user, password);
        identityResult.ThrowIfInvalid();

        // Generate email confirmation token
        var confirmationToken = await userManager.GenerateEmailConfirmationTokenAsync(user);

        // Send confirmation email
        await emailSender.SendConfirmation(user.Email!, user.UserName!, confirmationToken, cancellationToken);

        logger.LogInformation("User {UserName} registered successfully", user.UserName);
    }

    public async Task<string> Login(string username, string password, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByNameAsync(username);

        // Validate user password
        if (user is null || user.Suspended)
            throw new EnhancedException(MessageCodes.AccountLoginFailed);

        var identityResult = await signInManager.CheckPasswordSignInAsync(user, password, true);
        if (identityResult.Succeeded)
        {
            // Update user properties related to login
            user.LastLogin = DateTime.Now;
            user.LoginCount++;
            var updateResult = await userManager.UpdateAsync(user);
            updateResult.ThrowIfInvalid();

            // Generate JWT token
            var token = tokenGenerator.GenerateToken(user);
            await userManager.SetAuthenticationTokenAsync(user, "Default", "JWT", token);
            logger.LogInformation("User {UserName} authenticated successfully", user.UserName);
            return token;
        }
        else
        {
            throw new EnhancedException(MessageCodes.AccountLoginFailed);
        }
    }

    public async Task Logout(CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByNameAsync(securityContext.Username);
        if (user != null)
        {
            await userManager.RemoveAuthenticationTokenAsync(user, "Default", "JWT");
            logger.LogInformation("User {UserName} logged out successfully", user.UserName);
        }
    }

    public async Task ConfirmEmail(string username, string email, string emailToken, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByNameAsync(username) ??
            throw new EnhancedException(MessageCodes.AccountEmailConfirmedFailed);

        var result = await userManager.ConfirmEmailAsync(user, emailToken);
        result.ThrowIfInvalid();

        // Send welcome email
        await emailSender.SendWelcomeEmail(user.Email!, user.UserName!, cancellationToken);
        logger.LogInformation("User {UserName} confirmed email successfully", user.UserName);
    }

    public async Task ResendConfirmation(string email, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user == null)
        {
            // Don't reveal if user exists or not for security
            return;
        }

        if (user.EmailConfirmed)
        {
            // Email already confirmed
            return;
        }

        var confirmationToken = await userManager.GenerateEmailConfirmationTokenAsync(user);
        await emailSender.SendConfirmation(user.Email!, user.UserName!, confirmationToken, cancellationToken);
    }

    public async Task ForgotPassword(string email, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user == null)
        {
            // Don't reveal if user exists or not for security
            return;
        }

        var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
        await emailSender.SendPasswordReset(user.Email!, user.UserName!, resetToken, cancellationToken);

        logger.LogInformation("Password reset token sent to user {UserName}", user.UserName);

    }

    public async Task ResetPassword(string email, string token, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email) ??
            throw new EnhancedException(MessageCodes.AccountResetPasswordFailed);

        var result = await userManager.VerifyUserTokenAsync(user, "Default", "JWT", token);
        if (result)
        {
            var resetResult = await userManager.ResetPasswordAsync(user, token, newPassword);
            resetResult.ThrowIfInvalid();

            // Update user properties related to password changing
            user.PasswordChangedAt = DateTime.Now;
            user.PasswordChangedBy = securityContext.Username;
            await userManager.UpdateAsync(user);
            logger.LogInformation("User {UserName} reset password successfully", user.UserName);
        }
        return;
    }

    public async Task ChangePassword(string username, string oldPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByNameAsync(username) ??
            throw new EnhancedException(MessageCodes.AccountChangePasswordFailed);

        if (!await userManager.CheckPasswordAsync(user, oldPassword))
            throw new EnhancedException(MessageCodes.AccountChangePasswordFailed);

        var idResult = await userManager.ChangePasswordAsync(user, oldPassword, newPassword);

        idResult.ThrowIfInvalid();

        // Update user properties related to password changing
        user.PasswordChangedAt = DateTime.Now;
        user.PasswordChangedBy = securityContext.Username;
        var identityResult = await userManager.UpdateAsync(user);
        identityResult.ThrowIfInvalid();
        logger.LogInformation("User {UserName} changed password successfully", user.UserName);
    }
}

