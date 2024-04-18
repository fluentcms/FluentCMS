using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using System.Security.Claims;

namespace FluentCMS.Web.UI.Services;
public class AuthStateProvider(NavigationManager navigationManager, IJSRuntime jsRuntime, UserClient userClient, AccountClient accountClient) : AuthenticationStateProvider
{
    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var user = await FetchUserDetail();
            if (user == null)
            {
                return Anonymous();
            }
            return new AuthenticationState(GetClaimsPrincipal(user.Data));
        }
        catch (Exception e)
        {
            return Anonymous();
        }
    }

    private static AuthenticationState Anonymous()
    {
        return new AuthenticationState(new ClaimsPrincipal());
    }

    private async Task<UserDetailResponseIApiResult?> FetchUserDetail()
    {
        var cookie = await jsRuntime.GetCookieAsync<UserLoginResponse>();
        if (cookie is null)
        {
            return null;
        }
        var user = await userClient.GetAsync(cookie.UserId);
        return user;
    }

    public async Task<UserLoginResponseIApiResult> LoginAsync(UserLoginRequest userLoginRequest)
    {
        var result = await accountClient.AuthenticateAsync(userLoginRequest);

        if (result.Errors!.Count == 0)
        {
            await jsRuntime.SetCookieAsJsonAsync(result.Data, null);
        }
        return result;
    }

    public async Task LogoutAsync()
    {
        await jsRuntime.RemoveCookieAsync<UserLoginResponse>();
    }

    private ClaimsPrincipal GetClaimsPrincipal(UserDetailResponse userData)
    {
        return new ClaimsPrincipal(GetClaimIdentities(userData));
    }

    private IEnumerable<ClaimsIdentity> GetClaimIdentities(UserDetailResponse userData)
    {
        return [new ClaimsIdentity(GetClaims(userData), "Authentication")];
    }

    private IEnumerable<Claim>? GetClaims(UserDetailResponse userData)
    {
        // add id Username and Password claims with standard claim names
        yield return new Claim(ClaimTypes.Sid, userData.Id.ToString("D"));
        yield return new Claim(ClaimTypes.NameIdentifier, userData.Id.ToString("D"));
        if (userData.Username != null) yield return new Claim(ClaimTypes.Name, userData.Username);
        if (userData.PhoneNumber != null) yield return new Claim(ClaimTypes.MobilePhone, userData.PhoneNumber);
        if (userData.Email != null) yield return new Claim(ClaimTypes.Email, userData.Email);
        // add the rest of properties to the claims
        var excludedClaims = new List<string>() { nameof(userData.Username), nameof(userData.PhoneNumber), nameof(userData.Email), nameof(userData.Id) };
        foreach (var propertyInfo in userData.GetType().GetProperties().Where(x => !excludedClaims.Contains(x.Name)))
        {
            var value = propertyInfo.GetValue(userData)?.ToString();
            if (value != null) yield return new Claim(propertyInfo.Name, value);
        }
    }
}
