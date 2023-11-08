using FluentCMS.Entities;

namespace FluentCMS.Services;

public interface IApplicationContext
{
    public ICurrentContext Current { get; set; }
}

public interface ICurrentContext
{
    public User? User { get; set; }
    public List<Role> Roles { get; set; }
    public Host Host { get; set; }
    public Site Site { get; set; }
}