using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace FluentCMS.Web.Api;

public class AuthContext : IAuthContext
{
    private readonly string _username;

    private readonly Guid _userId;
    private readonly IEnumerable<Guid> _roleIds;
    private readonly bool _isSuperAdmin;
    private readonly bool _isAuthenticated;

    public AuthContext(IHttpContextAccessor httpContextAccessor)
    {
        var idClaimValue = httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.Sid)?.Value;
        _userId = idClaimValue == null ? Guid.Empty : Guid.Parse(idClaimValue);
        _username = httpContextAccessor.HttpContext?.User?.Identity?.Name ?? string.Empty;
        _roleIds = httpContextAccessor.HttpContext?.User?.Claims?.Where(x => x.Type == "role")?.Select(x => Guid.Parse(x.Value)) ?? [];
        _isSuperAdmin = httpContextAccessor.HttpContext?.User?.Claims?.Where(x => x.Type == "IsSuperAdmin")?.Select(x => bool.Parse(x.Value)).SingleOrDefault() ?? false;
        _isAuthenticated = httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
    }

    public IEnumerable<Guid> RoleIds => _roleIds;
    public string Username => _username;
    public bool IsAuthenticated => _isAuthenticated;
    public bool IsSuperAdmin => _isSuperAdmin;
    public Guid UserId => _userId;
}
