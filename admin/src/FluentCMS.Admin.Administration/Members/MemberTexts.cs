using FluentCMS.Admin.Administration.Common;
using FluentCMS.Admin.Localization;
using FluentCMS.Admin.Shell;
using Microsoft.Extensions.Localization;

namespace FluentCMS.Admin.Administration.Members;

// How the pages of the area name a member, its state and a group's role.
internal static class MemberTexts
{
    public static string Name(MemberRecord member) => string.IsNullOrWhiteSpace(member.DisplayName) ? member.Email : member.DisplayName;

    // A member as a question or a result names it: by its name and its address, which alone is unique.
    public static string Named(MemberRecord member, IStringLocalizer<MembersResources> texts) =>
        string.IsNullOrWhiteSpace(member.DisplayName) ? member.Email : texts["Members.Member.Named", member.DisplayName, member.Email];

    // A role of a site with its site, as RoleSelect names it, since a name is unique among the roles of one site only.
    public static string Role(MemberGroupRecord group, IStringLocalizer<CommonResources> common, AdminContext context) =>
        group.RoleSiteId is not { } site
            ? group.RoleName
            : common["Common.Roles.OfSite", group.RoleName, context.Sites.FirstOrDefault(item => item.Id == site)?.Name ?? site.ToString("D")];

    public static IEnumerable<string> States(MemberRecord member, IStringLocalizer<MembersResources> texts, FormatService formats, DateTimeOffset now)
    {
        yield return member.DeactivatedAt is null ? texts["Members.State.Active"] : texts["Members.State.Deactivated"];
        yield return member.EmailVerified ? texts["Members.State.Verified"] : texts["Members.State.NotVerified"];
        if (LockedOut(member, now) is { } end)
        {
            yield return texts["Members.State.LockedOut", formats.DateTime(end)];
        }
    }

    // A lockout in the past has ended, and the server keeps its time.
    public static DateTimeOffset? LockedOut(MemberRecord member, DateTimeOffset now) => member.LockoutEnd is { } end && end > now ? end : null;
}
