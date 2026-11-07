using FluentCMS.Authorization;
using FluentCMS.Sites;

namespace FluentCMS.Core.Authorization;

// The SDK's permission check for the services of plugins, with the rules of core's Management services.
internal sealed class PermissionChecker(ManagementAuthority authority) : IPermissionChecker
{
    public ValueTask<bool> AllowsAsync(string permission, SiteId? site, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(permission);

        return authority.AllowsAsync(permission, site?.Value, cancellationToken);
    }

    public async ValueTask RequireAsync(string permission, SiteId? site, CancellationToken cancellationToken = default)
    {
        if (!await AllowsAsync(permission, site, cancellationToken))
        {
            throw new PermissionDeniedException(permission, site);
        }
    }
}
