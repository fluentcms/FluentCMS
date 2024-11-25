namespace FluentCMS.Repositories.EFCore.DbModels;

[Table("Permissions")]
public class PermissionModel : SiteAssociatedEntityModel
{
    public Guid EntityId { get; set; }
    public string EntityType { get; set; } = default!;
    public string Action { get; set; } = default!; // comma separated list of actions
    public Guid RoleId { get; set; }
    public RoleModel Role { get; set; } = default!; // navigation property
}
