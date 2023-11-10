using FluentCMS.Entities;
using FluentCMS.Services;

namespace FluentCMS.Api;

public class ApiApplicationContext : IApplicationContext
{
    public required ICurrentContext Current { get; set; }
}

public class CurrentContext : ICurrentContext
{
    public User? User { get; set; }
    public List<Role> Roles { get; set; } = [];
    public required Host Host { get; set; }
    public required Site Site { get; set; }
    public string UserName => User?.UserName ?? string.Empty;
    public bool IsAuthenticated => !string.IsNullOrEmpty(UserName);
    public bool IsSuperAdmin => Host.SuperUsers.Contains(UserName);
}
