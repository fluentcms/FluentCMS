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
        var isSuperAdmin = _httpContextAccessor.HttpContext?.User?.Claims?.Where(x => x.Type == "issuperadmin")?.Select(x => bool.Parse(x.Value)).SingleOrDefault() ?? false;

        Current = new CurrentContext
        {
            Username = username,
            RoleIds = roleIds,
            IsSuperAdmin = isSuperAdmin
        };
    }
}

public class CurrentContext : ICurrentContext
{
    public IEnumerable<Guid> RoleIds { get; set; } = [];
    public string Username { get; set; } = string.Empty;
    public bool IsSuperAdmin { get; set; }
    public bool IsAuthenticated => !string.IsNullOrEmpty(Username);
}
