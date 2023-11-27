
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using SharpCompress.Common;
using System.Net;
using System.Security.Claims;
using System.Text.Json;

namespace FluentCMS.Web.UI;

public class FluentAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public FluentAuthenticationStateProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }
    public AuthenticationState Fail => new AuthenticationState(new System.Security.Claims.ClaimsPrincipal());
    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        //fetch access token from ProtectedLocalStorage
        try
        {
            var accessToken = GetAccessToken();
            var userId = GetUserId();
            var roleIds = GetRoleIds();
            //check access token retrieval success
          
                // fill claims
                var claims = new List<Claim>(){
                    new Claim("user-id", userId.ToString("D")),
                    new Claim(ClaimTypes.Role, JsonSerializer.Serialize(roleIds)),
                    new Claim(ClaimTypes.Name, userId.ToString("D")),
                    new Claim("access-token", accessToken),
                };
                var identity = new ClaimsIdentity(claims,"localStorage");
                var newState = new AuthenticationState(new ClaimsPrincipal(identity));
                NotifyAuthenticationStateChanged(Task.FromResult(newState));
                return newState;
            
        }
        catch (Exception)
        {

            
        }
        NotifyAuthenticationStateChanged(Task.FromResult(Fail));
        return Fail;
    }

    private Guid[] GetRoleIds()
    {
        return JsonSerializer.Deserialize<Guid[]>((_httpContextAccessor.HttpContext!.Request.Cookies["role-ids"]??""))??[];
    }

    private Guid GetUserId()
    {
        return JsonSerializer.Deserialize<Guid>((_httpContextAccessor.HttpContext!.Request.Cookies["user-id"] ?? ""));
    }

    private string GetAccessToken()
    {
        return _httpContextAccessor.HttpContext!.Request.Cookies["access-token"] ?? "";
        
    }
}
