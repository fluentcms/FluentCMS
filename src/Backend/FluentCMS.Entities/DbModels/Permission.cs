namespace FluentCMS.Repositories.EFCore.DbModels;

public class Permission : SiteAssociatedEntity
{
    public Guid EntityId { get; set; }
    public string EntityType { get; set; } = default!;
    public string Action { get; set; } = default!; // comma separated list of actions
    public Guid RoleId { get; set; }
}
