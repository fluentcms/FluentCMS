namespace FluentCMS.Core.Authorization;

// How a grant names permissions: a permission name, "*" for every permission, or a name with "*" in place of whole
// segments, such as content.*.read, which matches the permission of that name for any content type.
internal static class PermissionPattern
{
    private const string Wildcard = "*";

    public static bool IsWellFormed(string? grant)
    {
        if (string.IsNullOrEmpty(grant))
        {
            return false;
        }

        if (grant == CorePermissions.All)
        {
            return true;
        }

        var segments = grant.Split('.');
        return segments.Length >= 2 && segments.All(segment => segment == Wildcard || FluentCMS.Authorization.Permission.IsValidName("a." + segment));
    }

    public static bool Matches(string grant, string permission)
    {
        if (grant == CorePermissions.All)
        {
            return true;
        }

        var pattern = grant.Split('.');
        var name = permission.Split('.');
        if (pattern.Length != name.Length)
        {
            return false;
        }

        for (var index = 0; index < pattern.Length; index++)
        {
            if (pattern[index] != Wildcard && !string.Equals(pattern[index], name[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
