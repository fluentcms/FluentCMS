using FluentCMS.Entities;
using FluentCMS.Services;

namespace FluentCMS.Api;

//TODO: Nullables
public class ApiApplicationContext : IApplicationContext
{
    // todo: we should use IHttpContextAccessor to get the current HttpContext
    // and extract current user, role, host, etc...
    public ICurrentContext Current { get; set; }
}

public class CurrentContext : ICurrentContext
{
    public User? User { get; set; }
    public List<Role> Roles { get; set; } = [];
    public Host Host { get; set; }
    public Site Site { get; set; }
}
