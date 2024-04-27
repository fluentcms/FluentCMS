namespace FluentCMS.Entities;

public class Permission : AuditableEntity
{
    public string EntityType { get; set; } = default!;
    public Guid EntityId { get; set; } = default!;
    public List<Guid> RoleIds { get; set; } = [];
    public string Policy { get; set; } = default!;
}
