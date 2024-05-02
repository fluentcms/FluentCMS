using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace FluentCMS.Web.UI.Services;

public static class AuthenticationStateHelper
{
    public static async Task<UserLoginResponse?> GetLogin(this Task<AuthenticationState> authStateTask)
    {
        var authState = await authStateTask;

        if (authState?.User?.Identity?.IsAuthenticated == null)
            return default;

        var loginResponse = new UserLoginResponse
        {
            UserId = Guid.Parse(authState.User.FindFirstValue(ClaimTypes.Sid) ?? Guid.Empty.ToString()),
            Email = authState.User.FindFirstValue(ClaimTypes.Email),
            UserName = authState.User.FindFirstValue(ClaimTypes.NameIdentifier),
            Token = authState.User.FindFirstValue("jwt"),
            RoleIds = authState.User.FindAll(ClaimTypes.Role).Select(x => Guid.Parse(x.Value)).ToList()
        };
        return loginResponse;
    }
}
