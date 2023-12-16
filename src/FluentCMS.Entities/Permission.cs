namespace FluentCMS.Entities;

public class Permission : SiteAssociatedEntity
{
    public Guid RoleId { get; set; }
    public Guid EntityId { get; set; }
    public string EntityType { get; set; } = default!;
    public string Policy { get; set; } = default!;
}
