using FluentCMS.Web.UI.Services.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;

namespace FluentCMS.Web.UI.Services;

public class AuthStateProvider(
    ILocalStorageService localStorageService,
    AccountClient accountClient, HttpClient httpClient) :
    AuthenticationStateProvider()
{
    public const string LOCAL_STORAGE_KEY = "accessToken";

    public async Task<UserLoginResponseIApiResult> LoginAsync(UserLoginRequest body, CancellationToken cancellationToken = default)
    {
        // clear accessToken from local storage

        // try to authenticate from api and retrieve access token and user details
        var userLoginResponse = await accountClient.AuthenticateAsync(body, cancellationToken);
        if (userLoginResponse?.Data != null)
        {
            // serialize userLogin to json string
            var userLoginToken = JsonSerializer.Serialize(userLoginResponse.Data);

            // store access token in local storage
            await localStorageService.SetItemAsStringAsync(LOCAL_STORAGE_KEY, userLoginToken, cancellationToken);

            return userLoginResponse;
        }
        throw new Exception("Unable to login!");
    }

    public async Task<string?> GetUserToken(CancellationToken cancellationToken = default)
    {
        var userLogin = await localStorageService.GetItemAsync<UserLoginResponse>(LOCAL_STORAGE_KEY, cancellationToken);

        if (userLogin is null || string.IsNullOrEmpty(userLogin.Token))
            return null;

        return userLogin.Token;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            // read access token from local storage
            var userLogin = await localStorageService.GetItemAsync<UserLoginResponse>(LOCAL_STORAGE_KEY);

            if (userLogin != null)
            {
                // TODO: check token expiry
                SetupHttpClient(userLogin.Token);
                // read user details from api
                var userResponse = await accountClient.GetUserDetailAsync();
                if (userResponse.Data != null)
                {
                    // setting user detail claims
                    var user = userResponse.Data;
                    List<Claim> authClaims =
                    [
                        new Claim(ClaimTypes.Name, user.Username ?? string.Empty),
                        new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
                        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
                    ];

                    // setting user roles in claims
                    if (userLogin.RoleIds != null)
                    {
                        var roleClaims = userLogin.RoleIds.Select(x => new Claim(ClaimTypes.Role, x.ToString()));
                        authClaims.AddRange(roleClaims.ToList());
                    }

                    return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(authClaims, "Bearer")));
                }
            }
            return new AuthenticationState(new ClaimsPrincipal());
        }
        catch (Exception ex)
        {
            return new AuthenticationState(new ClaimsPrincipal());
        }
    }

    private void SetupHttpClient(string? userLoginToken)
    {
        // TODO: improve this
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userLoginToken);
    }

    public async Task LogoffAsync()
    {
        await localStorageService.RemoveItemAsync(LOCAL_STORAGE_KEY);
    }
}
