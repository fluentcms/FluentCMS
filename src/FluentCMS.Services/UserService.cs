using FluentCMS.Entities;
using FluentCMS.Providers.Identity;
using FluentCMS.Repositories;
using Microsoft.AspNetCore.Identity;
using UserToken = FluentCMS.Providers.Identity.UserToken;

namespace FluentCMS.Services;

public interface IUserService
{
    Task<IEnumerable<User>> GetAll(CancellationToken cancellationToken = default);
    Task<User> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<User> Update(User user, CancellationToken cancellationToken = default);
    Task<User> Create(User user, string password, CancellationToken cancellationToken = default);
    Task<User> Authenticate(string username, string password, CancellationToken cancellationToken = default);
    Task<UserToken> GetToken(User user, CancellationToken cancellationToken = default);
    Task ChangePassword(User user, string newPassword, CancellationToken cancellationToken = default);
    Task<User> ChangePassword(Guid id, string oldPassword, string newPassword, CancellationToken cancellationToken = default);
    Task<User?> GetByUsername(string username);
}

public class UserService : IUserService
{
    const string LOCAL_LOGIN_PROVIDER = "local";
    const string REFRESH_TOKEN_NAME = "refreshToken";

    private readonly UserManager<User> _userManager;
    private readonly IUserTokenProvider _userTokenProvider;
    private readonly IHostRepository _hostRepository;
    private readonly IApplicationContext _applicationContext;

    public UserService(UserManager<User> userManager, IUserTokenProvider userTokenProvider, IHostRepository hostRepository, IApplicationContext applicationContext)
    {
        _userManager = userManager;
        _userTokenProvider = userTokenProvider;
        _hostRepository = hostRepository;
        _applicationContext = applicationContext;
    }

    public async Task<User> Create(User user, string password, CancellationToken cancellationToken = default)
    {
        var identityResult = await _userManager.CreateAsync(user, password);
        identityResult.ThrowIfInvalid();
        return await GetById(user.Id, cancellationToken);
    }

    public async Task<UserToken> GetToken(User user, CancellationToken cancellationToken = default)
    {
        var isSuperAdmin = false;

        // check if user is superadmin
        if (!string.IsNullOrEmpty(user.UserName))
        {
            var host = await _hostRepository.Get(cancellationToken) ??
                throw new AppException(ExceptionCodes.HostNotFound);

            isSuperAdmin = host.SuperUsers.Contains(user.UserName);
        }

        // Generate token
        var userToken = await _userTokenProvider.Generate(user, isSuperAdmin);

        if (userToken is null || string.IsNullOrEmpty(userToken.AccessToken))
            throw new AppException(ExceptionCodes.UserTokenGenerationFailed);

        // Store refresh token
        var identityResult = await _userManager.SetAuthenticationTokenAsync(user, LOCAL_LOGIN_PROVIDER, REFRESH_TOKEN_NAME, userToken.RefreshToken);

        identityResult.ThrowIfInvalid();

        return userToken;
    }

    public async Task<User> Authenticate(string username, string password, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByNameAsync(username);

        // Validate user password
        if (user is null || !user.Enabled || !await _userManager.CheckPasswordAsync(user, password))
            throw new AppException(ExceptionCodes.UserLoginFailed);

        // Update user properties related to login
        user.LastLoginAt = DateTime.Now;
        user.LoginsCount++;
        await _userManager.UpdateAsync(user);

        return user;
    }

    public async Task ChangePassword(User user, string newPassword, CancellationToken cancellationToken = default)
    {
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);

        result.ThrowIfInvalid();
    }

    public async Task<User> Update(User entity, CancellationToken cancellationToken = default)
    {
        var result = await _userManager.UpdateAsync(entity);
        result.ThrowIfInvalid();
        return await GetById(entity.Id, cancellationToken);
    }

    public async Task<bool> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(id.ToString())
            ?? throw new AppException(ExceptionCodes.UserNotFound);

        var userRemoveResult = await _userManager.DeleteAsync(user);
        userRemoveResult.ThrowIfInvalid();

        return true;
    }

    public async Task<IEnumerable<User>> GetAll(CancellationToken cancellationToken = default)
    {
        // TODO: Check this: ToListAsync() is not working - throws exception! For this reason, the ToList() method is used
        return await Task.Run(() => _userManager.Users.ToList(), cancellationToken);
    }

    public async Task<User> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return await _userManager.FindByIdAsync(id.ToString())
            ?? throw new AppException(ExceptionCodes.UserNotFound);
    }

    public async Task<User> ChangePassword(Guid id, string oldPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await GetById(id, cancellationToken);

        if (!await _userManager.CheckPasswordAsync(user, oldPassword))
            throw new AppException(ExceptionCodes.UserChangePasswordFailed);

        var idResult = await _userManager.ChangePasswordAsync(user, oldPassword, newPassword);

        idResult.ThrowIfInvalid();

        // Update user properties related to password changing
        user.LastPasswordChangedAt = DateTime.Now;
        user.LastPasswordChangedBy = _applicationContext.Current.UserName;
        await _userManager.UpdateAsync(user);

        return user;
    }

    public Task<User?> GetByUsername(string username)
    {
        return _userManager.FindByNameAsync(username);
    }
}


public static class IdentityResultExtensions
{
    public static void ThrowIfInvalid(this IdentityResult identityResult)
    {
        if (!identityResult.Succeeded)
        {
            throw new AppException(identityResult.Errors.Select(e => $"User.{e.Code}"));
        }
    }
}
