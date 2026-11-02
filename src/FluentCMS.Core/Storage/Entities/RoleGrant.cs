namespace FluentCMS.Core.Storage.Entities;

// A row of cms_role_permission: one grant to a role. A role may hold the same permission in several rows with
// different constraints; the effective set is the union.
public sealed class RoleGrant
{
    public Guid Id { get; set; }

    public Guid RoleId { get; set; }

    // A permission name, "*", or a name with "*" for whole segments; see PermissionPattern.
    public string Permission { get; set; } = "";

    // JSON text of PermissionConstraints, null when the grant has none.
    public string? Constraints { get; set; }
}
