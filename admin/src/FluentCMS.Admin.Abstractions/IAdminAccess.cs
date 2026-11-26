using FluentCMS.Admin.Api;

namespace FluentCMS.Admin;

/// <summary>
/// What the signed-in user may do in the current site, from <c>GET /me/access</c>. Hiding a control the user may not use is
/// a convenience: the server checks every call, and a component still shows a 403 as the answer.
/// </summary>
public interface IAdminAccess
{
    /// <summary>Whether the user holds a permission in the current site or for the instance, with no limit to resources.</summary>
    /// <param name="permission">A permission name that is not per type, such as <c>users.manage</c> or a plugin's own.</param>
    /// <returns><see langword="true"/> when the user holds it; <see langword="false"/> otherwise, and until the access is read.</returns>
    bool Allows(string permission);

    /// <summary>
    /// Whether the user holds a permission for the instance, with no limit to resources. A page of an instance-only group,
    /// such as users or jobs, asks this: its calls answer 403 to a user who holds the permission in a site only.
    /// </summary>
    /// <param name="permission">A permission name that is not per type, such as <c>users.manage</c> or a plugin's own.</param>
    /// <returns><see langword="true"/> when the user holds it for the instance; <see langword="false"/> otherwise, and until the access is read.</returns>
    bool AllowsForInstance(string permission);

    /// <summary>
    /// Whether the user holds a permission in the current site or for the instance, with no limit to resources or for some
    /// resources only, as a grant limited to media folders gives it. A page or a control whose calls the plugin narrows
    /// resource by resource asks this, since the access does not say which resources a grant names: the calls answer for each.
    /// </summary>
    /// <param name="permission">A permission name that is not per type, such as <c>media.upload</c>.</param>
    /// <returns><see langword="true"/> when the user holds it for some resources at least; <see langword="false"/> otherwise, and until the access is read.</returns>
    bool AllowsForSomeResources(string permission);

    /// <summary>
    /// What the user may do with the entries of a content type in the current site and content locale, such as create or
    /// publish them. An operation counts when a grant gives it for the type, also one whose pattern names some types only,
    /// or one limited to locales or by a row rule; <see cref="TypeAccessInfo.Conditional"/> names those a single entry may
    /// still refuse. A content permission, such as <c>content.article.create</c>, is asked here, not of <see cref="Allows"/>.
    /// </summary>
    /// <param name="type">The name of the content type.</param>
    /// <returns>The access to the type, or <see langword="null"/> when the user may not read it, and until the access is read.</returns>
    TypeAccessInfo? ForType(string type);
}
