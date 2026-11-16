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
}
