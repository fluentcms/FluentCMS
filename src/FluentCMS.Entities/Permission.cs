namespace FluentCMS.Entities;

public class Permission : AuditEntity
{
    public Guid SiteId { get; set; }
    public Guid RoleId { get; set; }
    public Guid EntityId { get; set; }
    public string EntityType { get; set; } = default!;
    public string Policy { get; set; } = default!;
}
