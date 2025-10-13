namespace FluentCMS.Infrastructure.Identity.Models;

public abstract class RoleBase : RoleBase<RoleClaim>
{
    public RoleBase()
    {
    }
    public RoleBase(string roleName) : this()
    {
        Name = roleName;
    }
}

public class RoleBase<TRoleClaim> : IdentityRole<Guid>, IAuditableEntity where TRoleClaim : RoleClaim, new()
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

    public RoleBase()
    {
    }

    public RoleBase(string roleName) : this()
    {
        Name = roleName;
    }
}
