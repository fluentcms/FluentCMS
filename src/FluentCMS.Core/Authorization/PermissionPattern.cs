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

    // Whether every permission the other pattern matches, this one matches too.
    public static bool Covers(string grant, string other)
    {
        if (grant == CorePermissions.All)
        {
            return true;
        }

        if (other == CorePermissions.All)
        {
            return false;
        }

        var pattern = grant.Split('.');
        var narrower = other.Split('.');
        return pattern.Length == narrower.Length
            && pattern.Zip(narrower).All(pair => pair.First == Wildcard || string.Equals(pair.First, pair.Second, StringComparison.Ordinal));
    }

    // The pattern of the permissions both match, or null when none is matched by both.
    public static string? Intersect(string grant, string other)
    {
        if (grant == CorePermissions.All)
        {
            return other;
        }

        if (other == CorePermissions.All)
        {
            return grant;
        }

        var left = grant.Split('.');
        var right = other.Split('.');
        if (left.Length != right.Length)
        {
            return null;
        }

        var segments = new string[left.Length];
        for (var index = 0; index < left.Length; index++)
        {
            if (left[index] == Wildcard)
            {
                segments[index] = right[index];
            }
            else if (right[index] == Wildcard || string.Equals(left[index], right[index], StringComparison.Ordinal))
            {
                segments[index] = left[index];
            }
            else
            {
                return null;
            }
        }

        return string.Join('.', segments);
    }
}
