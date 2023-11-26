
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using SharpCompress.Common;
using System.Net;
using System.Security.Claims;
using System.Text.Json;

namespace FluentCMS.Web.UI;

public class FluentAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly ProtectedLocalStorage _protectedLocalStorage;

    public FluentAuthenticationStateProvider(ProtectedLocalStorage protectedLocalStorage)
    {
        _protectedLocalStorage = protectedLocalStorage;
    }
    public AuthenticationState Fail => new AuthenticationState(new System.Security.Claims.ClaimsPrincipal());
    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        //fetch access token from ProtectedLocalStorage
        try
        {
            var accessToken = await GetAccessToken();
            var userId = await GetUserId();
            var roleIds = await GetRoleIds();
            //check access token retrieval success
            if (accessToken.Success && !string.IsNullOrEmpty(accessToken.Value) && accessToken.Value is var token)
            {
                // fill claims
                var claims = new List<Claim>(){
                    new Claim("user-id", userId.Value.ToString("D")),
                    new Claim(ClaimTypes.Role, JsonSerializer.Serialize(roleIds.Value)),
                    new Claim(ClaimTypes.Name, userId.Value.ToString("D")),
                    new Claim("access-token", accessToken.Value),
                };
                var identity = new ClaimsIdentity(claims,"localStorage");
                var newState = new AuthenticationState(new ClaimsPrincipal(identity));
                NotifyAuthenticationStateChanged(Task.FromResult(newState));
                return newState;
            }
        }
        catch (Exception)
        {

            
        }
        NotifyAuthenticationStateChanged(Task.FromResult(Fail));
        return Fail;
    }

    private ValueTask<ProtectedBrowserStorageResult<Guid[]>> GetRoleIds()
    {
        return _protectedLocalStorage.GetAsync<Guid[]>("role-ids");
    }

    private ValueTask<ProtectedBrowserStorageResult<Guid>> GetUserId()
    {
        return _protectedLocalStorage.GetAsync<Guid>("user-id");
    }

    private ValueTask<ProtectedBrowserStorageResult<string>> GetAccessToken()
    {
        return _protectedLocalStorage.GetAsync<string>("access-token");
    }
}
