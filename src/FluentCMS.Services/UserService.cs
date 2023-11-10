using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using FluentCMS.Entities;
using FluentCMS.Services.Models;
using System.ComponentModel;
using FluentCMS.Providers.Identity;
using FluentCMS.Providers.Email;

namespace FluentCMS.Services;

public interface IUserService
{
    Task Create(User user, string password, CancellationToken cancellationToken = default);
    Task<User> Register(string username, string email, string password, CancellationToken cancellationToken = default);
    Task<UserSignInResult> Authenticate(string username, string password, CancellationToken cancellationToken = default);
    Task<Guid> GetCurrentUserId(CancellationToken cancellationToken = default);
    Task ChangePassword(User user, string newPassword, CancellationToken cancellationToken = default);
    Task ChangePassword(UserChangePassword changePassword, CancellationToken cancellationToken = default);
    Task ForgotPassword(string username, CancellationToken cancellationToken = default);
    Task ResetPassword(string username, string token, string newPassword, CancellationToken cancellationToken = default);
    Task RevokeTokens(Guid id, CancellationToken cancellationToken = default);
    Task<UserSignInResult> RefreshToken(string refreshToken, string expiredToken, CancellationToken cancellationToken = default);
    Task<IEnumerable<User>> GetAll(CancellationToken cancellationToken = default);
    Task<User> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Claim>> GetClaims(User user, CancellationToken cancellationToken = default);
    Task Update(User user, CancellationToken cancellationToken = default);
    Task<bool> ExistsUserName(string userName, CancellationToken cancellationToken = default);
}

public class UserService : IUserService
{
    const string LOCAL_LOGIN_PROVIDER = "local";
    const string REFRESH_TOKEN_NAME = "refreshToken";

    private readonly UserManager<User> _userManager;
    private readonly IUserTokenProvider _userTokenProvider;
    private readonly IApplicationContext _appContext;
    private readonly IEmailProvider _emailProvider;

    public UserService(UserManager<User> userManager, IUserTokenProvider userTokenProvider, IApplicationContext appContext, IEmailProvider emailProvider)
    {
        _userManager = userManager;
        _appContext = appContext;
        _userTokenProvider = userTokenProvider;
        _emailProvider = emailProvider;
    }

    public async Task Create(User user, string password, CancellationToken cancellationToken = default)
    {
        var identityResult = await _userManager.CreateAsync(user, password);
        identityResult.ThrowIfInvalid();
    }

    public async Task<User> Register(string username, string email, string password, CancellationToken cancellationToken = default)
    {
        var user = new User
        {
            UserName = username,
            Email = email,
            EmailConfirmed = false,
            PhoneNumberConfirmed = false,
            Enabled = true
        };
        await Create(user, password, cancellationToken);
        return user;
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
            Token = token.AccessToken,
            RefreshToken = token.RefreshToken,
            Expiry = token.Expiry
        };
    }

    public Task<Guid> GetCurrentUserId(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Guid.Empty);
        //if (Accessor.HttpContext == null) return default;

        //var userId = UserManager.GetUserId(Accessor.HttpContext.User);
        //return string.IsNullOrEmpty(userId) ? default : Task.FromResult(userId.GetTypedKey<TUserKey>());
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

    public async Task Update(User entity, CancellationToken cancellationToken = default)
    {
        await _userManager.UpdateAsync(entity);
    }

    public async Task<bool> ExistsUserName(string userName, CancellationToken cancellationToken = default)
    {
        userName = userName.ToUpperInvariant();
        return await _userManager.FindByNameAsync(userName) != null;
    }

    public async Task<bool> Delete(Guid id, CancellationToken cancellationToken = default)
    {
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
        return _userManager.FindByIdAsync(id.ToString());
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

    public async Task ForgotPassword(string username, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByNameAsync(username);
        if (user == null) throw new Exception("User does not exist!");

        var resetPasswordToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        await SendResetPasswordToken(user, resetPasswordToken);
    }

    protected async Task SendResetPasswordToken(User user, string token)
    {
        // Send email in background to prevent in interruptions
        var backgroundWorker = new BackgroundWorker();
        backgroundWorker.DoWork += async (_, _) => await _emailProvider.Send(user.Email, ForgotPasswordMessage.Subject, ForgotPasswordMessage.GetBodyWithReplaces(token));
        backgroundWorker.RunWorkerAsync();
        await Task.CompletedTask;
    }

    public async Task ResetPassword(string username, string token, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByNameAsync(username);
        if (user == null) throw new Exception("User does not exist!");

        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
        result.ThrowIfInvalid();

        // Update user properties related to password changing
        user.LastPasswordChangedAt = DateTime.Now;
        //user.LastPasswordChangedBy = AppContext.UserName;
        await _userManager.UpdateAsync(user);
    }

    public async Task RevokeTokens(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null) return;

        var identityResult = await _userManager.ResetAuthenticatorKeyAsync(user);
        identityResult.ThrowIfInvalid();

    }

    public async Task<UserSignInResult> RefreshToken(string refreshToken, string expiredToken, CancellationToken cancellationToken = default)
    {
        var userId = await _userTokenProvider.ValidateExpiredToken(expiredToken);
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (!user.Enabled) throw new Exception("User is disabled!");

        var storedRefreshToken = await _userManager.GetAuthenticationTokenAsync(user, LOCAL_LOGIN_PROVIDER, REFRESH_TOKEN_NAME);
        if (storedRefreshToken != refreshToken) throw new Exception("Token expired!");

        var newToken = await _userTokenProvider.Generate(user);

        // Update user properties related to login
        user.LastLoginAt = DateTime.Now;
        user.LoginsCount++;
        await _userManager.UpdateAsync(user);

        return new UserSignInResult
        {
            UserId = user.Id,
            RoleIds = user.RoleIds,
            Token = newToken.AccessToken,
            RefreshToken = refreshToken,
            Expiry = newToken.Expiry
        };
    }
}