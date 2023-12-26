using Microsoft.AspNetCore.Http;

namespace FluentCMS.Web.Api;

public class AuthContext : IAuthContext
{
    private string _username;
    private readonly IEnumerable<Guid> _roleIds;
    private bool _isSuperAdmin;
    private bool _isAuthenticated;

    public AuthContext(IHttpContextAccessor httpContextAccessor)
    {
        _username = httpContextAccessor.HttpContext?.User?.Identity?.Name ?? string.Empty;
        _roleIds = httpContextAccessor.HttpContext?.User?.Claims?.Where(x => x.Type == "role")?.Select(x => Guid.Parse(x.Value)) ?? Enumerable.Empty<Guid>();
        _isSuperAdmin = httpContextAccessor.HttpContext?.User?.Claims?.Where(x => x.Type == "IsSuperAdmin")?.Select(x => bool.Parse(x.Value)).SingleOrDefault() ?? false;
        _isAuthenticated = httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
    }

    public IEnumerable<Guid> RoleIds => _roleIds;
    public string Username => _username;
    public bool IsAuthenticated => _isAuthenticated;
    public bool IsSuperAdmin => _isSuperAdmin;
}
