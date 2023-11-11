using Microsoft.AspNetCore.Identity;
using FluentCMS.Entities;
using FluentCMS.Providers.Identity;
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
}

public class UserService : IUserService
{
    const string LOCAL_LOGIN_PROVIDER = "local";
    const string REFRESH_TOKEN_NAME = "refreshToken";

    private readonly UserManager<User> _userManager;
    private readonly IUserTokenProvider _userTokenProvider;

    public UserService(UserManager<User> userManager, IUserTokenProvider userTokenProvider)
    {
        _userManager = userManager;
        _userTokenProvider = userTokenProvider;
    }

    public async Task<User> Create(User user, string password, CancellationToken cancellationToken = default)
    {
        var identityResult = await _userManager.CreateAsync(user, password);
        identityResult.ThrowIfInvalid();
        return await GetById(user.Id, cancellationToken);
    }

    public async Task<UserToken> GetToken(User user, CancellationToken cancellationToken = default)
    {
        // Generate token
        var userToken = await _userTokenProvider.Generate(user);

        if (userToken is null || string.IsNullOrEmpty(userToken.AccessToken))
            throw new Exception("Token generation failed!");

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
            throw new Exception("User doesn't exist or username/password is not valid!");

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

        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null) throw new Exception("User doesn't exist!");

        var userRemoveResult = await _userManager.DeleteAsync(user);
        userRemoveResult.ThrowIfInvalid();

        return true;
    }

    public async Task<IEnumerable<User>> GetAll(CancellationToken cancellationToken = default)
    {
        // TODO: Check this: ToListAsync() is not working - throws exception! For this reason, the ToList() method is used
        return await Task.Run(() => _userManager.Users.ToList(), cancellationToken);
    }

    public Task<User> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return _userManager.FindByIdAsync(id.ToString()) ?? throw new Exception("User doesn't exist!");
    }

    public async Task<User> ChangePassword(Guid id, string oldPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await GetById(id, cancellationToken);

        if (!await _userManager.CheckPasswordAsync(user, oldPassword))
            throw new Exception("User doesn't exist or username/password is not valid!");

        var idResult = await _userManager.ChangePasswordAsync(user, oldPassword, newPassword);

        idResult.ThrowIfInvalid();

        // Update user properties related to password changing
        user.LastPasswordChangedAt = DateTime.Now;
        //user.LastPasswordChangedBy = AppContext.UserName;
        await _userManager.UpdateAsync(user);

        return user;
    }
}