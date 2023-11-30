using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Claims;

namespace FluentCMS.Web.UI;

public class FluentCmsAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public FluentCmsAuthenticationStateProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }
    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        // fetch access-token from cookies
        var accessToken = _httpContextAccessor.HttpContext?.Request.Cookies["access-token"];

        // if no access-token in cookies
        if (string.IsNullOrEmpty(accessToken))
        {
            //fail auth
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal()));
        }

        // get claims from access-token
        var claims = new JsonWebToken(accessToken).Claims;

        // authenticated with access-token
        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, "access-token"))));
    }
}
