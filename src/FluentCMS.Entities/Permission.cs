namespace FluentCMS.Entities;

public class Permission : AuditableEntity
{
    public Guid RoleId { get; set; } = default!;
    public string Area { get; set; } = default!;
    public string Action { get; set; } = default!;
}
