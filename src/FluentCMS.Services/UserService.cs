﻿using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Services;

public interface IUserService : IAutoRegisterService
{
    Task<User> Authenticate(string username, string password, CancellationToken cancellationToken = default);
    Task<IEnumerable<User>> GetAll(CancellationToken cancellationToken = default);
    Task<User> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<User> Update(User user, CancellationToken cancellationToken = default);
    Task<User> Create(User user, string password, CancellationToken cancellationToken = default);
    Task<UserToken> GetToken(User user, CancellationToken cancellationToken = default);
    Task ChangePassword(User user, string newPassword, CancellationToken cancellationToken = default);
    Task<User> ChangePassword(Guid id, string oldPassword, string newPassword, CancellationToken cancellationToken = default);
    Task<string> GenerateToken(string purpose, User user, CancellationToken cancellationToken = default);
    Task<bool> ValidateToken(string token, string purpose, User user, CancellationToken cancellationToken = default);
    Task<string> GeneratePasswordResetToken(string email, CancellationToken cancellationToken = default);
    Task<bool> ValidatePasswordResetToken(string token, string email, string newPassword, CancellationToken cancellationToken = default);
    Task<bool> Any(CancellationToken cancellationToken = default);
    Task<User?> GetByEmail(string requestEmail);
}

public class UserService(
    IGlobalSettingsRepository globalSettingsRepository,
    IUserTokenProvider userTokenProvider,
    UserManager<User> userManager,
    IAuthContext authContext) : IUserService
{
    public const string LOCAL_LOGIN_PROVIDER = "local";
    public const string REFRESH_TOKEN_NAME = "refreshToken";
    public const string PASSWORD_RESET_PURPOSE = "passwordReset";

    public async Task<User> Create(User user, string password, CancellationToken cancellationToken = default)
    {
        var identityResult = await userManager.CreateAsync(user, password);
        identityResult.ThrowIfInvalid();
        return await GetById(user.Id, cancellationToken);
    }

    public async Task<UserToken> GetToken(User user, CancellationToken cancellationToken = default)
    {
        var isSuperAdmin = false;

        // check if user is super admin
        if (!string.IsNullOrEmpty(user.UserName))
        {
            var globalSettings = await globalSettingsRepository.Get(cancellationToken) ??
                throw new AppException(ExceptionCodes.GlobalSettingsNotFound);

            isSuperAdmin = globalSettings.SuperUsers.Contains(user.UserName);
        }

        // Generate token
        var userToken = await userTokenProvider.Generate(user, isSuperAdmin);

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

    public async Task ChangePassword(User user, string newPassword, CancellationToken cancellationToken = default)
    {
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, newPassword);

        result.ThrowIfInvalid();
    }

    public async Task<User> Update(User entity, CancellationToken cancellationToken = default)
    {
        var prevUser = await GetById(entity.Id, cancellationToken);
        entity = Merge(prevUser, entity);
        var result = await userManager.UpdateAsync(entity);
        result.ThrowIfInvalid();
        return await GetById(entity.Id, cancellationToken);
    }

    public async Task<bool> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByIdAsync(id.ToString())
            ?? throw new AppException(ExceptionCodes.UserNotFound);

        var userRemoveResult = await userManager.DeleteAsync(user);
        userRemoveResult.ThrowIfInvalid();

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
        user.PasswordChangedBy = authContext.Username;
        await userManager.UpdateAsync(user);

        return user;
    }

    public async Task<string> GenerateToken(string purpose, User user, CancellationToken cancellationToken = default)
    {
        return await userManager.GenerateUserTokenAsync(user, GetTokenProvider(purpose), purpose);
    }

    public static string GetTokenProvider(string purpose)
    {
        return $"{purpose}Provider";
    }

    public async Task<bool> ValidateToken(string token, string purpose, User user, CancellationToken cancellationToken = default)
    {
        return await userManager.VerifyUserTokenAsync(user, GetTokenProvider(purpose), purpose, token);
    }

    public async Task<string> GeneratePasswordResetToken(string email, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email) ?? throw new AppException(ExceptionCodes.UserNotFound);
        return await GenerateToken(PASSWORD_RESET_PURPOSE, user);
    }

    public async Task<bool> ValidatePasswordResetToken(string token, string email, string newPassword, CancellationToken cancellationToke = default)
    {
        var user = await userManager.FindByEmailAsync(email) ?? throw new AppException(ExceptionCodes.UserNotFound);
        var result = await ValidateToken(token, PASSWORD_RESET_PURPOSE, user);
        if (result)
        {
            await ChangePassword(user, newPassword, cancellationToke);
        }
        return result;
    }

    public Task<bool> Any(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => userManager.Users.Any(), cancellationToken);
    }

    public Task<User?> GetByEmail(string requestEmail)
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
