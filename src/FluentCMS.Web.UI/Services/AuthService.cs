using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace FluentCMS.Web.UI.Services;

public interface IAuthService
{
    Task Login(HttpContext httpContext, string username, string password, bool isPersist);
    Task Logout(HttpContext httpContext);
    Task<UserLoginResponse> GetLogin();
}

public class AuthService : IAuthService
{
    private readonly AccountClient _accountClient;
    private readonly Task<AuthenticationState> _authenticationStateTask;

    public AuthService(AccountClient accountClient, Task<AuthenticationState> authenticationStateTask)
    {
        _accountClient = accountClient;
        _authenticationStateTask = authenticationStateTask;
    }

    public async Task<UserLoginResponse> GetLogin()
    {
        var authState = await _authenticationStateTask;
        var loginResponse = new UserLoginResponse
        {
            UserId = Guid.Parse(authState.User.FindFirstValue(ClaimTypes.Sid) ?? Guid.Empty.ToString()),
            Email = authState.User.FindFirstValue(ClaimTypes.Email),
            UserName = authState.User.FindFirstValue(ClaimTypes.NameIdentifier),
            Token = authState.User.FindFirstValue("jwt"),
            RoleIds = authState.User.FindAll(ClaimTypes.Role).Select(x => Guid.Parse(x.Value)).ToList()
        };
        return loginResponse;
    }

    public async Task Logout(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public async Task Login(HttpContext httpContext, string username, string password, bool isPersist)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var accountResponse = await _accountClient.AuthenticateAsync(new UserLoginRequest
        {
            Username = username,
            Password = password,
        });

        var account = accountResponse.Data ?? throw new Exception("Something bad happened!");

        var identityClaims = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);

        identityClaims.AddClaim(new Claim(ClaimTypes.Sid, account.UserId.ToString()));
        identityClaims.AddClaim(new Claim(ClaimTypes.NameIdentifier, account.UserName ?? string.Empty));
        identityClaims.AddClaim(new Claim(ClaimTypes.Email, account.Email ?? string.Empty));
        identityClaims.AddClaim(new Claim("jwt", account.Token ?? string.Empty));

        foreach (var role in account?.RoleIds ?? [])
            identityClaims.AddClaim(new Claim(ClaimTypes.Role, role.ToString()));

        var cookieClaims = new ClaimsPrincipal(identityClaims);

        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            cookieClaims,
            new AuthenticationProperties
            {
                IsPersistent = true,
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(10) // 100 days
            });

        httpContext.Response.Redirect("/");
    }
}
