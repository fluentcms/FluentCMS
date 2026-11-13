using FluentCMS.Sites;

namespace FluentCMS.Authorization;

/// <summary>
/// Checks a permission of the principal of the current operation, so that a plugin's service enforces its own permissions
/// as core's services do: the HTTP layer carries the identity and the service decides. A check needs a current operation:
/// code that acts as the system, such as a job, a seed or the host's own startup code, runs inside
/// <see cref="FluentCMS.Content.SystemOperation.Begin"/>, which every check allows. A grant limited to resources, such as
/// media folders, counts only in <see cref="ResourcesAsync"/>, because the other checks cannot say which resource they act on.
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

    /// <summary>
    /// The resources in a site that the principal holds a permission for, for a plugin whose grants can be limited to its own
    /// resources, such as the folders of the media plugin. The ids are the plugin's: core stores and compares them as text.
    /// </summary>
    /// <param name="permission">The name of the permission, such as <c>media.read</c>.</param>
    /// <param name="site">The site, or <see langword="null"/> for the instance as a whole, which only a grant with no site limit reaches.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>
    /// <see langword="null"/> when a grant with no resource limit holds the permission there, so that every resource is reached;
    /// otherwise the union of the resources named by the grants that hold it, which is empty when none does.
    /// </returns>
    /// <exception cref="InvalidOperationException">There is no current operation.</exception>
    ValueTask<IReadOnlySet<string>?> ResourcesAsync(string permission, SiteId? site, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the principal may manage: whether its token or key is for managing, by the <c>management</c> scope (or the
    /// <c>mcp</c> scope on the MCP server), or it is the system. A principal that may not, such as the anonymous caller,
    /// a Delivery or preview key, a preview token or a token without the scope, holds no permission but reading content,
    /// whatever its roles grant, so every other check refuses it. A plugin whose answer to such a reader is the one the
    /// Delivery API gives anyone reads this, as the media plugin does for media references.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns><see langword="true"/> when a grant may give the principal more than reading content.</returns>
    /// <exception cref="InvalidOperationException">There is no current operation.</exception>
    ValueTask<bool> CanManageAsync(CancellationToken cancellationToken = default);
}
