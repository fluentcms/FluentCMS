using Microsoft.AspNetCore.Http;

namespace FluentCMS.Api;

public class ApiApplicationContext : IApplicationContext
{
    public required ICurrentContext Current { get; set; }

    private readonly IHttpContextAccessor _httpContextAccessor;

    public ApiApplicationContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;

        var username = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? string.Empty;
        var roleIds = _httpContextAccessor.HttpContext?.User?.Claims?.Where(x => x.Type == "role")?.Select(x => Guid.Parse(x.Value)) ?? [];

        Current = new CurrentContext
        {
            UserName = username,
            RoleIds = roleIds
        };
    }
}

public class CurrentContext : ICurrentContext
{
    public IEnumerable<Guid> RoleIds { get; set; } = [];
    public string UserName { get; set; } = string.Empty;
    public bool IsSuperAdmin { get; set; }
    public bool IsAuthenticated => !string.IsNullOrEmpty(UserName);
    
    public bool IsInRole(Guid roleId)
    {
        if (RoleIds == null || !RoleIds.Any())
            return false;

        return RoleIds.Any(x => x == roleId);
    }

    public bool IsInRole(IEnumerable<Guid> roleIds)
    {
        return roleIds.Any(IsInRole);
    }
}
