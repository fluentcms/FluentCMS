namespace FluentCMS.Core.Storage.Entities;

// A row of cms_role.
public sealed class Role
{
    public Guid Id { get; set; }

    // Null for an instance-wide role.
    public Guid? SiteId { get; set; }

    public string Name { get; set; } = "";

    public string? Description { get; set; }

    // Seeded by the system migration; role management must not delete or rename it.
    public bool BuiltIn { get; set; }

    // A user who holds the role gets a session only after a second factor.
    public bool RequireTwoFactor { get; set; }

    // Goes up with every change to the role or its grants, so that a cache keyed by (role id, version) goes stale.
    // RoleVersionInterceptor writes it; nothing else should.
    public long PermissionsVersion { get; set; } = 1;
}
