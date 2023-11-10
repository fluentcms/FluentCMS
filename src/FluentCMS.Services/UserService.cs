using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using FluentCMS.Entities;
using FluentCMS.Services.Models;
using FluentCMS.Providers.Identity;

namespace FluentCMS.Services;

public interface IUserService
{
    Task<IEnumerable<User>> GetAll(CancellationToken cancellationToken = default);
    Task<User> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Claim>> GetClaims(User user, CancellationToken cancellationToken = default);
    Task<User> Update(User user, CancellationToken cancellationToken = default);
    Task<User> Create(User user, string password, CancellationToken cancellationToken = default);
    Task<UserSignInResult> Authenticate(string username, string password, CancellationToken cancellationToken = default);
    Task ChangePassword(User user, string newPassword, CancellationToken cancellationToken = default);
    Task ChangePassword(UserChangePassword changePassword, CancellationToken cancellationToken = default);
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

    public async Task<UserSignInResult> Authenticate(string username, string password, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByNameAsync(username);

        // Validate user password
        if (user is null || !user.Enabled || !await _userManager.CheckPasswordAsync(user, password)) throw new Exception("User doesn't exist or username/password is not valid!");

        // Generate token
        var token = await _userTokenProvider.Generate(user);

        // Store refresh token
        var identityResult = await _userManager.SetAuthenticationTokenAsync(user, LOCAL_LOGIN_PROVIDER, REFRESH_TOKEN_NAME, token.RefreshToken);
        identityResult.ThrowIfInvalid();

        // Update user properties related to login
        user.LastLoginAt = DateTime.Now;
        user.LoginsCount++;
        await _userManager.UpdateAsync(user);

        return new UserSignInResult
        {
            UserId = user.Id,
            RoleIds = user.RoleIds,
            Token = token.AccessToken
        };
    }

    public async Task ChangePassword(User user, string newPassword, CancellationToken cancellationToken = default)
    {
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);

        result.ThrowIfInvalid();
    }

    public async Task<IEnumerable<Claim>> GetClaims(User user, CancellationToken cancellationToken = default)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.NormalizedUserName)
        };
        if (!string.IsNullOrWhiteSpace(user.NormalizedEmail)) claims.Add(new Claim(ClaimTypes.Email, user.NormalizedEmail));
        if (!string.IsNullOrWhiteSpace(user.PhoneNumber)) claims.Add(new Claim(ClaimTypes.Email, user.PhoneNumber));

        var userRoles = await _userManager.GetRolesAsync(user);
        var userRoleClaims = userRoles.Select(userRole => new Claim(ClaimTypes.Role, userRole));
        claims.AddRange(userRoleClaims);

        return claims;
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

    public async Task ChangePassword(UserChangePassword changePassword, CancellationToken cancellationToken = default)
    {
        var user = await GetById(changePassword.UserId, cancellationToken);

        if (changePassword.UserId == null || user == null || !await _userManager.CheckPasswordAsync(user, changePassword.CurrentPassword))
            throw new Exception("User doesn't exist or username/password is not valid!");

        var idResult = await _userManager.ChangePasswordAsync(user, changePassword.CurrentPassword, changePassword.NewPassword);

        idResult.ThrowIfInvalid();

        // Update user properties related to password changing
        user.LastPasswordChangedAt = DateTime.Now;
        //user.LastPasswordChangedBy = AppContext.UserName;
        await _userManager.UpdateAsync(user);
    }

}