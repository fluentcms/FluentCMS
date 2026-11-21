using FluentCMS.Admin.Administration.Common;
using FluentCMS.Admin.Shell;

namespace FluentCMS.Admin.Administration.Roles;

// Which roles the caller manages, and which is a Public role. GET /roles also answers the roles the caller may only read or
// hand out, such as the roles of every site to one who holds roles.manage in its site, so the pages narrow what it answers:
// roles.manage for the instance manages every role, and in a site that site's roles only, as the server checks a write
// where the role applies.
internal static class RoleScope
{
    public const string Permission = "roles.manage";

    public static readonly Guid SuperAdmin = new("00000000-0000-7000-8000-000000000001");

    // The Public role of every site that has none of its own.
    public static readonly Guid SeededPublic = new("00000000-0000-7000-8000-000000000004");

    private const string PublicName = "Public";

    public static bool ForInstance(AdminContext context) => context.AllowsForInstance(Permission);

    public static bool Manages(AdminContext context, Guid? siteId) =>
        ForInstance(context) || (siteId is { } site && site == context.Site?.Id && context.Allows(Permission));

    public static bool Manages(AdminContext context, RoleSummary role) => Manages(context, role.SiteId);

    // The role anyone holds who has not signed in: a site's role named Public, as the server finds it, or the seeded one.
    public static bool IsPublic(Guid id, Guid? siteId, string name) =>
        id == SeededPublic || (siteId is not null && string.Equals(name, PublicName, StringComparison.Ordinal));
}
