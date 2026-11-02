namespace FluentCMS.Authorization;

/// <summary>
/// Declares the permissions of a plugin. Register an implementation in the service collection; its permissions join the
/// catalogue that roles grant from, next to the core ones and the ones generated for each content type.
/// </summary>
public interface IPermissionProvider
{
    /// <summary>Lists the permissions.</summary>
    /// <returns>
    /// The permissions. A name that the core catalogue or another provider already declares, or one under the reserved
    /// <see cref="Permission.ContentPrefix"/>, stops the host at startup.
    /// </returns>
    IEnumerable<Permission> GetPermissions();
}
