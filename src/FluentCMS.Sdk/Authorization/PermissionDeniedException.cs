using FluentCMS.Sites;

namespace FluentCMS.Authorization;

/// <summary>Thrown when the principal does not hold a permission an operation needs. The HTTP layer maps it to a 401 or a 403.</summary>
public sealed class PermissionDeniedException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="permission">The name of the permission.</param>
    /// <param name="site">The site it was needed in, or <see langword="null"/> for the instance as a whole.</param>
    public PermissionDeniedException(string permission, SiteId? site)
        : base(site is { } one
            ? $"This needs the permission {permission} in the site {one.Value:D}."
            : $"This needs the instance-wide permission {permission}.")
    {
        Permission = permission;
        Site = site;
    }

    /// <summary>The name of the permission.</summary>
    public string Permission { get; }

    /// <summary>The site, or <see langword="null"/> for the instance as a whole.</summary>
    public SiteId? Site { get; }
}
