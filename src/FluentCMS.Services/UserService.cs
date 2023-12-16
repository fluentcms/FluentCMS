using FluentCMS.Entities;
using FluentCMS.Providers.Identity;
using FluentCMS.Repositories;
using Microsoft.AspNetCore.Identity;
using UserToken = FluentCMS.Providers.Identity.UserToken;

namespace FluentCMS.Services;

public interface IUserService
{
    Task<User> Login(string username, string password, CancellationToken cancellationToken = default);
    Task<IEnumerable<User>> GetAll(CancellationToken cancellationToken = default);
    Task<User> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<User> Update(User user, CancellationToken cancellationToken = default);
    Task<User> Create(User user, string password, CancellationToken cancellationToken = default);
    Task<UserToken> GetToken(User user, CancellationToken cancellationToken = default);
    Task ChangePassword(User user, string newPassword, CancellationToken cancellationToken = default);
    Task<User> ChangePassword(Guid id, string oldPassword, string newPassword, CancellationToken cancellationToken = default);
}

public class UserService(
    UserManager<User> userManager,
    IUserTokenProvider userTokenProvider,
    IHostRepository hostRepository,
    IApplicationContext applicationContext) : IUserService
{
    const string LOCAL_LOGIN_PROVIDER = "local";
    const string REFRESH_TOKEN_NAME = "refreshToken";

    public async Task<User> Create(User user, string password, CancellationToken cancellationToken = default)
    {
        var identityResult = await userManager.CreateAsync(user, password);
        identityResult.ThrowIfInvalid();
        return await GetById(user.Id, cancellationToken);
    }

    public async Task<UserToken> GetToken(User user, CancellationToken cancellationToken = default)
    {
        var isSuperAdmin = false;

        // check if user is superadmin
        if (!string.IsNullOrEmpty(user.UserName))
        {
            var host = await hostRepository.Get(cancellationToken) ??
                throw new AppException(ExceptionCodes.HostNotFound);

            isSuperAdmin = host.SuperUsers.Contains(user.UserName);
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

    public async Task<User> Login(string username, string password, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByNameAsync(username);

        // Validate user password
        if (user is null || !user.Enabled || !await userManager.CheckPasswordAsync(user, password))
            throw new AppException(ExceptionCodes.UserLoginFailed);

        // Update user properties related to login
        user.LastLoginAt = DateTime.Now;
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
        // TODO: Check this: ToListAsync() is not working - throws exception! For this reason, the ToList() method is used
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
        user.LastPasswordChangedAt = DateTime.Now;
        user.LastPasswordChangedBy = applicationContext.Current.Username;
        await userManager.UpdateAsync(user);

        return user;
    }
}
