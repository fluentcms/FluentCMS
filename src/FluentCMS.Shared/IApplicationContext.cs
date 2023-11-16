using FluentCMS.Entities;

namespace FluentCMS;

public interface IApplicationContext
{
    public ICurrentContext Current { get; set; }
}

public interface ICurrentContext
{
    public User? User { get; set; }
    public List<Guid> RoleIds { get; set; }
    public Host Host { get; set; }
    public string UserName { get; }
    public bool IsAuthenticated { get; }
    public bool IsSuperAdmin { get; }
    public bool IsInRole(Guid roleId);
    public bool IsInRole(IEnumerable<Guid> roleIds);
}
