namespace FluentCMS.Entities;

public class Policy : AuditableEntity
{
    public List<Guid> RoleIds { get; set; } = [];
    public string Area { get; set; } = default!;
}
