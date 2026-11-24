using FluentCMS.Admin.Administration.Common;
using FluentCMS.Admin.Localization;
using FluentCMS.Admin.Shell;
using Microsoft.Extensions.Localization;

namespace FluentCMS.Admin.Administration.Users;

// How the list and the detail name a user's roles and state.
internal static class UserTexts
{
    // A role of a site with its site, as RoleSelect names it, since a name is unique among the roles of one site only.
    public static string Role(string name, Guid? siteId, IStringLocalizer<CommonResources> common, AdminContext context) =>
        siteId is not { } site
            ? name
            : common["Common.Roles.OfSite", name, context.Sites.FirstOrDefault(item => item.Id == site)?.Name ?? site.ToString("D")];

    public static IEnumerable<string> States(UserRecord user, IStringLocalizer<UsersResources> texts, FormatService formats, DateTimeOffset now)
    {
        yield return user.DeactivatedAt is null ? texts["Users.State.Active"] : texts["Users.State.Deactivated"];
        if (LockedOut(user, now) is { } end)
        {
            yield return texts["Users.State.LockedOut", formats.DateTime(end)];
        }

        if (user.TwoFactorEnabled)
        {
            yield return texts["Users.State.TwoFactor"];
        }

        if (user.MustChangePassword)
        {
            yield return texts["Users.State.MustChangePassword"];
        }
    }

    // A lockout in the past has ended, and the server keeps its time.
    public static DateTimeOffset? LockedOut(UserRecord user, DateTimeOffset now) => user.LockoutEnd is { } end && end > now ? end : null;
}
