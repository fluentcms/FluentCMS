namespace FluentCMS.Entities;

public class Permission : SiteAssociatedEntity
{
    public Guid EntityId { get; set; }
    public string EntityType { get; set; } = default!;
    public string Action { get; set; } = default!;
    public Guid RoleId { get; set; }
}
