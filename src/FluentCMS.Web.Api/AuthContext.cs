using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace FluentCMS.Web.Api;

public class AuthContext : IAuthContext
{
    private readonly string _username;
    private readonly Guid _userId;
    private readonly bool _isAuthenticated;

    public AuthContext(IHttpContextAccessor httpContextAccessor)
    {
        var idClaimValue = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Sid);
        _userId = idClaimValue == null ? Guid.Empty : Guid.Parse(idClaimValue);
        _username = httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        _isAuthenticated = httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
    }

    public string Username => _username;
    public bool IsAuthenticated => _isAuthenticated;
    public Guid UserId => _userId;
}
