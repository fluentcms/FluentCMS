using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace FluentCMS.Web.ApiClients.Services;

public class AuthManager(ApiClientFactory apiClient)
{
    public async Task Logout(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public async Task Login(HttpContext httpContext, string username, string password, bool isPersist)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var accountResponse = await apiClient.Account.AuthenticateAsync(new UserLoginRequest
        {
            Username = username,
            Password = password,
        });

        var account = accountResponse.Data ?? throw new InvalidOperationException("Something bad happened!");

        var identityClaims = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);

        identityClaims.AddClaim(new Claim(ClaimTypes.Sid, account.UserId.ToString()));
        identityClaims.AddClaim(new Claim(ClaimTypes.NameIdentifier, account.UserName ?? string.Empty));
        identityClaims.AddClaim(new Claim(ClaimTypes.Email, account.Email ?? string.Empty));
        identityClaims.AddClaim(new Claim("jwt", account.Token ?? string.Empty));

        var cookieClaims = new ClaimsPrincipal(identityClaims);

        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            cookieClaims,
            new AuthenticationProperties
            {
                IsPersistent = true,
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(10) // 100 days
            });
    }
}
