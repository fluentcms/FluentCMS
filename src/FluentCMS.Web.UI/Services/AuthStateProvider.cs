using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using FluentCMS.Web.UI.Services.LocalStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Web.UI.Services;
public class AuthStateProvider(NavigationManager navigationManager, IHttpContextAccessor httpContextAccessor, UserClient userClient, AccountClient accountClient, ILoggerFactory factory) : RevalidatingServerAuthenticationStateProvider(factory)
{
    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var user = await FetchUserDetail();
            if (user == null)
            {
                return NotAuthorized();
            }
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

    private async Task<UserDetailResponseIApiResult?> FetchUserDetail()
    {
        var json = httpContextAccessor.HttpContext?.Request?.Cookies["UserLoginResponse"];
        if (json != null)
        {
            var loginResponse =
                JsonSerializer.Deserialize<UserLoginResponse?>(json);
            
            if (loginResponse?.UserId is var userId && userId is null || userId == default(Guid))
            {
                return null;
            }
            var user = await userClient.GetAsync(loginResponse.UserId);
            return user;
        }

        return null;
    }

    public async Task<UserLoginResponseIApiResult> LoginAsync(UserLoginRequest userLoginRequest)
    {
        var result = await accountClient.AuthenticateAsync(userLoginRequest);

        if (result.Errors!.Count == 0)
        {
            navigationManager.NavigateTo($"/api/auth?user-id={result.Data.UserId}&token={result.Data.Token}&role-ids={JsonSerializer.Serialize(result.Data.RoleIds)}", true);
        }
        return result;
    }

    public async Task Logout()
    {
        navigationManager.NavigateTo($"/api/auth/logout", true);
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(new ClaimsPrincipal()))); // not authorized
    }

    private ClaimsPrincipal GetClaimsPricipal(UserDetailResponse userData)
    {
        return new ClaimsPrincipal(GetClaimIdentities(userData));
    }

    private IEnumerable<ClaimsIdentity> GetClaimIdentities(UserDetailResponse userData)
    {
        return [new ClaimsIdentity(GetClaims(userData), "localStorage")];
    }

    private IEnumerable<Claim>? GetClaims(UserDetailResponse userData)
    {
        // static claims
        if (userData.Id != null) yield return new Claim(ClaimTypes.Sid, userData.Id.ToString("D"));
        if (userData.Id != null) yield return new Claim(ClaimTypes.NameIdentifier, userData.Id.ToString("D"));
        if (userData.Username != null) yield return new Claim(ClaimTypes.Name, userData.Username);
        if (userData.PhoneNumber != null) yield return new Claim(ClaimTypes.MobilePhone, userData.PhoneNumber);
        if (userData.Email != null) yield return new Claim(ClaimTypes.Email, userData.Email);
        // other claims excluding username, phone and email
        var excludedClaims = new List<string>() { nameof(userData.Username), nameof(userData.PhoneNumber), nameof(userData.Email), nameof(userData.Id) };
        foreach (var propertyInfo in userData.GetType().GetProperties().Where(x => !excludedClaims.Contains(x.Name)))
        {
            var value = propertyInfo.GetValue(userData)?.ToString();
            if (value != null) yield return new Claim(propertyInfo.Name, value);
        }
    }

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        return true;
    }

    protected override TimeSpan RevalidationInterval { get; } = TimeSpan.FromMinutes(30);
}
