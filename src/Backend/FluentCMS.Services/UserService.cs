using FluentCMS.Providers.EmailProviders;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace FluentCMS.Services;

public interface IUserService : IAutoRegisterService
{
    Task<User> Authenticate(string username, string password, CancellationToken cancellationToken = default);
    Task<IEnumerable<User>> GetAll(CancellationToken cancellationToken = default);
    Task<User> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<User> Update(User user, CancellationToken cancellationToken = default);
    Task<User> Create(User user, string password, CancellationToken cancellationToken = default);
    Task<UserToken> GetToken(User user, CancellationToken cancellationToken = default);
    Task<bool> ChangePassword(User user, string newPassword, CancellationToken cancellationToken = default);
    Task<User> ChangePassword(Guid id, string oldPassword, string newPassword, CancellationToken cancellationToken = default);
    Task<bool> SendPasswordResetToken(string email, CancellationToken cancellationToken = default);
    Task<bool> ChangePasswordByResetToken(string email, string token, string newPassword, CancellationToken cancellationToken = default);
    Task<bool> Any(CancellationToken cancellationToken = default);
    Task<User?> GetByEmail(string requestEmail, CancellationToken cancellationToken = default);
}

public class UserService(
    IGlobalSettingsRepository globalSettingsRepository,
    IUserTokenProvider userTokenProvider,
    UserManager<User> userManager,
    IEmailProvider emailProvider,
    IConfiguration configuration,
    IApiExecutionContext apiExecutionContext,
    IMessagePublisher messagePublisher) : IUserService
{
    public const string LOCAL_LOGIN_PROVIDER = "local";
    public const string REFRESH_TOKEN_NAME = "refreshToken";
    public const string PASSWORD_RESET_PURPOSE = "passwordReset";
    public const string PASSWORD_RESET_TOKEN_PROVIDER = "passwordResetProvider";

    public async Task<User> Create(User user, string password, CancellationToken cancellationToken = default)
    {
        var identityResult = await userManager.CreateAsync(user, password);
        identityResult.ThrowIfInvalid();

        var newUser = await GetById(user.Id, cancellationToken);

        await messagePublisher.Publish(new Message<User>(ActionNames.UserCreated, newUser), cancellationToken);

        return newUser;
    }

    public async Task<UserToken> GetToken(User user, CancellationToken cancellationToken = default)
    {
        // check if user is super admin
        if (!string.IsNullOrEmpty(user.UserName))
        {
            var globalSettings = await globalSettingsRepository.Get(cancellationToken) ??
                throw new AppException(ExceptionCodes.GlobalSettingsNotFound);
        }

        // Generate token
        var userToken = await userTokenProvider.Generate(user);

        if (userToken is null || string.IsNullOrEmpty(userToken.AccessToken))
            throw new AppException(ExceptionCodes.UserTokenGenerationFailed);

        // Store refresh token
        var identityResult = await userManager.SetAuthenticationTokenAsync(user, LOCAL_LOGIN_PROVIDER, REFRESH_TOKEN_NAME, userToken.RefreshToken);

        identityResult.ThrowIfInvalid();

        return userToken;
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

        return user;
    }

    public async Task<bool> ChangePassword(User user, string newPassword, CancellationToken cancellationToken = default)
    {
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, newPassword);

        result.ThrowIfInvalid();
        return true;
    }

    public async Task<User> Update(User user, CancellationToken cancellationToken = default)
    {
        var prevUser = await GetById(user.Id, cancellationToken);
        user = Merge(prevUser, user);
        var result = await userManager.UpdateAsync(user);
        result.ThrowIfInvalid();

        var updated = await GetById(user.Id, cancellationToken);

        await messagePublisher.Publish(new Message<User>(ActionNames.UserUpdated, updated), cancellationToken);

        return updated;
    }

    public async Task<bool> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByIdAsync(id.ToString())
            ?? throw new AppException(ExceptionCodes.UserNotFound);

        var globalSettings = await globalSettingsRepository.Get(cancellationToken) ??
            throw new AppException(ExceptionCodes.GlobalSettingsNotFound);

        if (globalSettings.SuperAdmins.Contains(user.UserName))
            throw new AppException(ExceptionCodes.UserSuperAdminCanNotBeDeleted);

        var userRemoveResult = await userManager.DeleteAsync(user);
        userRemoveResult.ThrowIfInvalid();

        await messagePublisher.Publish(new Message<User>(ActionNames.UserDeleted, user), cancellationToken);

        return true;
    }

    public async Task<IEnumerable<User>> GetAll(CancellationToken cancellationToken = default)
    {
        // TODO: Check this: ToListAsync() is not working - throws exception!
        // For this reason, the ToList() method is used
        return await Task.Run(() => userManager.Users.ToList(), cancellationToken);
    }

    public async Task<User> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return await userManager.FindByIdAsync(id.ToString())
            ?? throw new AppException(ExceptionCodes.UserNotFound);
    }

    public async Task<User> ChangePassword(Guid id, string oldPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await GetById(id, cancellationToken);

        if (!await userManager.CheckPasswordAsync(user, oldPassword))
            throw new AppException(ExceptionCodes.UserChangePasswordFailed);

        var idResult = await userManager.ChangePasswordAsync(user, oldPassword, newPassword);

        idResult.ThrowIfInvalid();

        // Update user properties related to password changing
        user.PasswordChangedAt = DateTime.Now;
        user.PasswordChangedBy = apiExecutionContext.Username;
        await userManager.UpdateAsync(user);

        return user;
    }

    public async Task<bool> SendPasswordResetToken(string email, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email) ??
            throw new AppException(ExceptionCodes.UserNotFound);

        var token = await userManager.GenerateUserTokenAsync(user, PASSWORD_RESET_TOKEN_PROVIDER, PASSWORD_RESET_PURPOSE);

        await emailProvider.Send(email, "Reset Password", $"{configuration["urls"]}/auth/reset-password?token={token}&email={email}", cancellationToken);

        return true;
    }

    public async Task<bool> ChangePasswordByResetToken(string email, string token, string newPassword, CancellationToken cancellationToke = default)
    {
        var user = await userManager.FindByEmailAsync(email) ?? throw new AppException(ExceptionCodes.UserNotFound);
        var result = await userManager.VerifyUserTokenAsync(user, PASSWORD_RESET_TOKEN_PROVIDER, PASSWORD_RESET_PURPOSE, token);
        if (result)
        {
            await ChangePassword(user, newPassword, cancellationToke);
        }
        return result;
    }

    // TODO: remove this unused method
    public Task<bool> Any(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => userManager.Users.Any(), cancellationToken);
    }

    public Task<User?> GetByEmail(string requestEmail, CancellationToken cancellationToken = default)
    {
        return userManager.FindByEmailAsync(requestEmail);
    }

    public static T Merge<T>(T target, T source)
    {
        var type = typeof(T);
        var properties = type.GetProperties();
        foreach (var property in properties)
        {
            // ignore if null
            if (property.GetValue(source) == null) continue;
            property.SetValue(target, property.GetValue(source));
        }
        return target;
    }

}
