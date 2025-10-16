namespace FluentCMS.Api.Core.Models.Identity;

public class Role : IdentityRole<Guid>, ISiteAssociatedEntity
{
    public string Description { get; set; } = string.Empty;

    public RoleTypes Type { get; set; } = RoleTypes.UserDefined;

    // IAuditableEntity implementations
    public string? CreatedBy { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int Version { get; set; }
    public Guid SiteId { get; set; }

    public Role()
    {
    }

    public Role(string roleName) : this()
    {
        Name = roleName;
    }
}
