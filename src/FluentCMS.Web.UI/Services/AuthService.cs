using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;

namespace FluentCMS.Web.UI.Services;

public interface IAuthService
{
    Task Login(HttpContext httpContext, string username, string password, bool isPersist);
    Task Logout(HttpContext httpContext);
}

public class AuthService(IHttpClientFactory httpClientFactory) : IAuthService
{
    public async Task Logout(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public async Task Login(HttpContext httpContext, string username, string password, bool isPersist)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var httpClient = httpClientFactory.CreateClient("FluentCMS.Web.Api");
        var accountClient = new AccountClient(httpClient);
        var accountResponse = await accountClient.AuthenticateAsync(new UserLoginRequest
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

        httpContext.Response.Redirect("/admin");
    }
}
