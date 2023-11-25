namespace FluentCMS;

public interface IApplicationContext
{
    public ICurrentContext Current { get; set; }
}

public interface ICurrentContext
{
    public IEnumerable<Guid> RoleIds { get; set; }
    public string UserName { get; set; }
    public bool IsAuthenticated { get; }
    public bool IsSuperAdmin { get; set; }
}
