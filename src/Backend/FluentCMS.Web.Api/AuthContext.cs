using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace FluentCMS.Web.Api;

public class AuthContext : IAuthContext
{
    public AuthContext(IHttpContextAccessor httpContextAccessor)
    {
        var user = httpContextAccessor.HttpContext?.User;

        if (user is null)
            return;

        var idClaimValue = user.FindFirstValue(ClaimTypes.Sid);

        UserId = idClaimValue == null ? Guid.Empty : Guid.Parse(idClaimValue);
        Username = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        IsAuthenticated = user.Identity?.IsAuthenticated ?? false;
    }

    public string Username { get; private set; } = string.Empty;
    public bool IsAuthenticated { get; private set; } = false;
    public Guid UserId { get; private set; } = Guid.Empty;
}
