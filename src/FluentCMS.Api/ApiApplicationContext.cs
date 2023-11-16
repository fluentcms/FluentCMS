using FluentCMS.Entities;
using FluentCMS.Repositories;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Api;

// TODO: revise this implementation
public class ApiApplicationContext : IApplicationContext
{
    public required ICurrentContext Current { get; set; }

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IHostRepository _hostRepository;
    private readonly IUserRepository _userRepository;

    public ApiApplicationContext(IHttpContextAccessor httpContextAccessor, IHostRepository hostRepository, IUserRepository userRepository)
    {
        _httpContextAccessor = httpContextAccessor;
        _hostRepository = hostRepository;
        _userRepository = userRepository;
        var username = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? string.Empty;
        User currentUser = default;
        if (!string.IsNullOrEmpty(username))
        {
            currentUser = _userRepository.FindByName(username.ToUpper()).Result;
        }

        Current = new CurrentContext
        {
            Host = _hostRepository.Get().Result,
            User = currentUser,
            RoleIds = currentUser?.RoleIds ?? []
        };
    }
}

public class CurrentContext : ICurrentContext
{
    public User? User { get; set; }
    public List<Guid> RoleIds { get; set; } = [];
    public required Host Host { get; set; }
    public string UserName => User?.UserName ?? string.Empty;
    public bool IsAuthenticated => !string.IsNullOrEmpty(UserName);
    public bool IsSuperAdmin => Host.SuperUsers.Contains(UserName);

    public bool IsInRole(Guid roleId)
    {
        if (IsSuperAdmin)
            return true;

        if (RoleIds == null || !RoleIds.Any())
            return false;

        return RoleIds.Any(x => x == roleId);
    }

    public bool IsInRole(IEnumerable<Guid> roleIds)
    {
        if (IsSuperAdmin)
            return true;

        return roleIds.Any(IsInRole);
    }
}
