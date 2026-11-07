using FluentCMS.Sites;

namespace FluentCMS.Authorization;

/// <summary>
/// Checks a permission of the principal of the current operation, so that a plugin's service enforces its own permissions
/// as core's services do: the HTTP layer carries the identity and the service decides. A check needs a current operation:
/// code that acts as the system, such as a job, a seed or the host's own startup code, runs inside
/// <see cref="FluentCMS.Content.SystemOperation.Begin"/>, which every check allows.
/// </summary>
public interface IPermissionChecker
{
    /// <summary>Whether the principal holds a permission in a site.</summary>
    /// <param name="permission">The name of the permission, such as <c>media.manage</c>.</param>
    /// <param name="site">The site, or <see langword="null"/> for the instance as a whole, which only a grant with no site limit reaches.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns><see langword="true"/> when a grant of the principal allows the permission there.</returns>
    /// <exception cref="InvalidOperationException">There is no current operation.</exception>
    ValueTask<bool> AllowsAsync(string permission, SiteId? site, CancellationToken cancellationToken = default);

    /// <summary>Refuses the operation unless the principal holds a permission in a site.</summary>
    /// <param name="permission">The name of the permission, such as <c>media.manage</c>.</param>
    /// <param name="site">The site, or <see langword="null"/> for the instance as a whole, which only a grant with no site limit reaches.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>A task that completes when the permission is held.</returns>
    /// <exception cref="PermissionDeniedException">The principal does not hold the permission there.</exception>
    /// <exception cref="InvalidOperationException">There is no current operation.</exception>
    ValueTask RequireAsync(string permission, SiteId? site, CancellationToken cancellationToken = default);
}
