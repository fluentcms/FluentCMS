using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace FluentCMS.Web.UI.Services;

public interface IAuthService
{
    Task Login(HttpContext httpContext, string username, string password, bool isPersist);
    Task Logout(HttpContext httpContext);
    Task<UserLoginResponse?> GetLogin();
}

public class AuthService(AccountClient accountClient, AuthenticationStateProvider authenticationStateProvider) : IAuthService
{
    public async Task Logout(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public async Task Login(HttpContext httpContext, string username, string password, bool isPersist)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var accountResponse = await accountClient.AuthenticateAsync(new UserLoginRequest
        {
            Username = username,
            Password = password,
        });

        var account = accountResponse.Data ?? throw new Exception("Something bad happened!");

        var identityClaims = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);

        identityClaims.AddClaim(new Claim(ClaimTypes.NameIdentifier, account.UserId.ToString()));
        identityClaims.AddClaim(new Claim(ClaimTypes.Email, account.Email ?? string.Empty));
        identityClaims.AddClaim(new Claim(ClaimTypes.Name, account.UserName ?? string.Empty));
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

    public async Task<UserLoginResponse?> GetLogin()
    {
        if (
            authenticationStateProvider is null ||
            await authenticationStateProvider.GetAuthenticationStateAsync() is var userState &&
            (!userState.User.Identity?.IsAuthenticated ?? false))
        {
            return null;
        }
        ClaimsPrincipal user = userState.User;
        return new UserLoginResponse()
        {
            Email = user.FindFirstValue(ClaimTypes.Email),
            RoleIds = user.FindFirstValue(ClaimTypes.Role)?.Split(",").Select(x => Guid.Parse(x)).ToArray(),
            Token = user.FindFirstValue("jwt"),
            UserName = user.FindFirstValue(ClaimTypes.Name),
            UserId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!)
        };
    }
}
