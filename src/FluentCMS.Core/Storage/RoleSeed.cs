using FluentCMS.Core.Authorization;
using FluentCMS.Core.Storage.Entities;

namespace FluentCMS.Core.Storage;

// The rows that the system migration inserts. A change here is a model change, so the snapshot tests fail until a
// migration carries it. Ids are fixed: roles by BuiltInRoles, grants by position in the list.
internal static class RoleSeed
{
    private static readonly string OwnEntries = new PermissionConstraints { RowRule = BuiltInRoles.OwnEntriesRule }.ToJson()!;

    private static readonly (Guid Role, string Permission, string? Constraints)[] Grants =
    [
        // Everything, including the permissions of plugins and content types that do not exist yet.
        (BuiltInRoles.SuperAdmin, CorePermissions.All, null),

        (BuiltInRoles.Editor, "content.*.create", null),
        (BuiltInRoles.Editor, "content.*.read", null),
        (BuiltInRoles.Editor, "content.*.update", null),
        (BuiltInRoles.Editor, "content.*.delete", null),
        (BuiltInRoles.Editor, "content.*.publish", null),
        (BuiltInRoles.Editor, "content.*.unpublish", null),
        (BuiltInRoles.Editor, "content.*.restore", null),
        (BuiltInRoles.Editor, CorePermissions.SchemaRead, null),

        (BuiltInRoles.Author, "content.*.create", null),
        (BuiltInRoles.Author, "content.*.read", OwnEntries),
        (BuiltInRoles.Author, "content.*.update", OwnEntries),
        (BuiltInRoles.Author, "content.*.delete", OwnEntries),
        (BuiltInRoles.Author, CorePermissions.SchemaRead, null),

        // Public and Member start with nothing: anonymous Delivery access opens only when a role grants it.
    ];

    public static IEnumerable<Role> Roles() =>
    [
        Built(BuiltInRoles.SuperAdmin, BuiltInRoles.SuperAdminName, "Holds every permission."),
        Built(BuiltInRoles.Editor, BuiltInRoles.EditorName, "Creates, edits, publishes and removes any content."),
        Built(BuiltInRoles.Author, BuiltInRoles.AuthorName, "Creates content, and reads, edits or removes only their own entries."),
        Built(BuiltInRoles.Public, BuiltInRoles.PublicName, "The anonymous visitor. Grants here open public Delivery access."),
        Built(BuiltInRoles.Member, BuiltInRoles.MemberName, "The default role of a registered member."),
    ];

    public static IEnumerable<RoleGrant> Permissions() =>
        Grants.Select((grant, index) => new RoleGrant
        {
            Id = new Guid($"00000000-0000-7000-8001-{index + 1:D12}"),
            RoleId = grant.Role,
            Permission = grant.Permission,
            Constraints = grant.Constraints,
        });

    private static Role Built(Guid id, string name, string description) =>
        new() { Id = id, Name = name, Description = description, BuiltIn = true, PermissionsVersion = 1 };
}
