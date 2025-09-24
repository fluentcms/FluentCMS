namespace FluentCMS.Identity;

public class Role : Role<RoleClaim>
{
    public Role()
    {
    }
    public Role(string roleName) : this()
    {
        Name = roleName;
    }
}

public class Role<TRoleClaim> : IdentityRole<Guid>, IAuditableEntity where TRoleClaim : RoleClaim, new()
{
    public string Description { get; set; } = string.Empty;
    public RoleTypes Type { get; set; } = RoleTypes.UserDefined;

    // IAuditableEntity implementations
    public string? CreatedBy { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int Version { get; set; }

    public Role()
    {
    }

    public Role(string roleName) : this()
    {
        Name = roleName;
    }
}
