using System.Reflection;
using System.Security.Claims;
using FluentCMS.Web.UI.Services.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;

namespace FluentCMS.Web.UI.Services;
public class AuthStateProvider(ILocalStorageService localStorageService, UserClient userClient, AccountClient accountClient) : AuthenticationStateProvider
{
    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var user = await FetchUserDetail();
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(GetClaimsPricipal(user.Data))));
            return new AuthenticationState(GetClaimsPricipal(user.Data));
        }
        catch (Exception e)
        {
            return NotAuthorized(); // not authorized
        }
    }

    private static AuthenticationState NotAuthorized()
    {
        return new AuthenticationState(new ClaimsPrincipal());
    }

    private async Task<UserDetailResponseIApiResult> FetchUserDetail()
    {
        var loginResponse =
            await localStorageService.GetItemAsync<UserLoginResponse>(LocalStorageKeys.UserLoginResponse);
        var user = await userClient.GetAsync(loginResponse.UserId);
        return user;
    }

    public async Task<UserLoginResponseIApiResult> LoginAsync(UserLoginRequest userLoginRequest)
    {
        var result = await accountClient.AuthenticateAsync(userLoginRequest);

        if (result.Errors!.Count == 0)
        {
            await localStorageService.SetItemAsync(LocalStorageKeys.UserLoginResponse, result.Data);
            var user = await FetchUserDetail();
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(GetClaimsPricipal(user.Data))));
        }
        return result;
    }

    public async Task Logout()
    {
        await localStorageService.ClearAsync();
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(new ClaimsPrincipal()))); // not authorized
    }

    private ClaimsPrincipal GetClaimsPricipal(UserDetailResponse userData)
    {
        return new ClaimsPrincipal(GetClaimIdentities(userData));
    }

    private IEnumerable<ClaimsIdentity> GetClaimIdentities(UserDetailResponse userData)
    {
        return [new ClaimsIdentity(GetClaims(userData),"localStorage")];
    }

    private IEnumerable<Claim>? GetClaims(UserDetailResponse userData)
    {
        // static claims
        if (userData.Username != null) yield return new Claim(ClaimTypes.Name, userData.Username);
        if (userData.PhoneNumber != null) yield return new Claim(ClaimTypes.MobilePhone, userData.PhoneNumber);
        if (userData.Email != null) yield return new Claim(ClaimTypes.Email, userData.Email);
        // other claims excluding username, phone and email
        var excludedClaims = new List<string>() { nameof(userData.Username), nameof(userData.PhoneNumber), nameof(userData.Email) };
        foreach (var propertyInfo in userData.GetType().GetProperties().Where(x => !excludedClaims.Contains(x.Name)))
        {
            var value = propertyInfo.GetValue(userData)?.ToString();
            if (value != null) yield return new Claim(propertyInfo.Name, value);
        }
    }
}
