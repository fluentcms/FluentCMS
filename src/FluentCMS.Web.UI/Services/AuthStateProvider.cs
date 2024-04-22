using FluentCMS.Web.UI.Services.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using System.Text.Json;
using System.Web;

namespace FluentCMS.Web.UI.Services;

public class AuthStateProvider(ICookieService cookieService, IHttpClientFactory httpClientFactory) : AuthenticationStateProvider
{
    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var loginResponse = await GetIdentityFromCookie();

        if (loginResponse == null)
            return new AuthenticationState(new ClaimsPrincipal());

        return new AuthenticationState(GetClaimsPrincipal(loginResponse));
    }

    public async Task<UserLoginResponseIApiResult> Login(UserLoginRequest userLoginRequest)
    {
        var result = await httpClientFactory.GetClient<AccountClient>().AuthenticateAsync(userLoginRequest);

        if (result.Errors!.Count == 0 && result.Data != null)
        {
            var jsonData = JsonSerializer.Serialize(result.Data);

            await cookieService.SetAsync(nameof(UserLoginResponse), HttpUtility.UrlEncode(jsonData), null);

            var authState = new AuthenticationState(GetClaimsPrincipal(result.Data));

            NotifyAuthenticationStateChanged(Task.FromResult(authState));
        }
        return result;
    }

    public async Task Logout()
    {
        await cookieService.RemoveAsync(nameof(UserLoginResponse));
        var authState = new AuthenticationState(new ClaimsPrincipal());
        NotifyAuthenticationStateChanged(Task.FromResult(authState));
    }

    private static ClaimsPrincipal GetClaimsPrincipal(UserLoginResponse loginResponse)
    {
        var claims = new List<Claim>()
        {
            new(ClaimTypes.Sid, loginResponse.UserId.ToString()),
            new(ClaimTypes.NameIdentifier, loginResponse.UserName ?? string.Empty),
            new(ClaimTypes.Email, loginResponse.Email ?? string.Empty)
        };

        if (loginResponse.RoleIds != null)
            foreach (var roleId in loginResponse.RoleIds)
                claims.Add(new(ClaimTypes.Role, roleId.ToString()));

        var claimsIdentity = new ClaimsIdentity(claims, "Bearer");

        return new ClaimsPrincipal([claimsIdentity]);
    }

    private async Task<UserLoginResponse?> GetIdentityFromCookie()
    {
        var cookie = await cookieService.GetAsync(nameof(UserLoginResponse));

        if (cookie is null || string.IsNullOrEmpty(cookie.Value))
            return null;
        try
        {
            var jsonData = HttpUtility.UrlDecode(cookie.Value);

            var loginResponse = JsonSerializer.Deserialize<UserLoginResponse>(jsonData);

            if (loginResponse is null)
                return null;

            return loginResponse;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
