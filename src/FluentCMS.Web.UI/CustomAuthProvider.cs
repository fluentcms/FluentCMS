using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using System.Text.Json;

namespace FluentCMS.Web.UI;

public class CustomAuthProvider : AuthenticationStateProvider
{
    private readonly ILocalStorageService _localStorageService;
    public CustomAuthProvider(ILocalStorageService localStorageService)
    {
        _localStorageService = localStorageService;
    }
    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var jwtToken = await _localStorageService.GetItemAsync<string>("jwt-access-token");
        if (string.IsNullOrEmpty(jwtToken))
        {
            return new AuthenticationState(
                new ClaimsPrincipal(new ClaimsIdentity()));
        }

        return new AuthenticationState(new ClaimsPrincipal(
            new ClaimsIdentity(ParseClaimsFromJwt(jwtToken), "JwtAuth")));
    }

    private static IEnumerable<Claim> ParseClaimsFromJwt(string jwt)
    {
        var payload = jwt.Split('.')[1];
        var jsonBytes = ParseBase64WithoutPadding(payload);
        var keyValuePairs = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonBytes);
        return keyValuePairs.Select(kvp => new Claim(kvp.Key, kvp.Value.ToString()));
    }

    private static byte[] ParseBase64WithoutPadding(string base64)
    {
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        return Convert.FromBase64String(base64);
    }

    public void NotifyAuthState()
    {
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
