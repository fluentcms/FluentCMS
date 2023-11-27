namespace FluentCMS.Entities;

public class Role : AuditEntity, IAuthorizeEntity
{
    public Guid SiteId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public RoleType Type { get; set; }
}

public enum RoleType
{
    Normal = 0,
    Authenticated = 1,
    Guest = 2,
    All = 3
}
