using FluentCMS.Content;
using FluentCMS.Sites;

namespace FluentCMS.Core.Authorization;

// The Management API's refusal of a principal without the permission a call needs: 403, or 401 when it is not signed in.
internal sealed class ManagementAccessDeniedException(string message) : Exception(message);

// The permission checks of the services the Management API calls: users, roles, keys, sites, jobs and the activity log.
// A null site is the instance as a whole, which only a grant with no site limit reaches. Without an operation the call is
// the host's own code, which these services already take as the system for their audit columns.
internal sealed class ManagementAuthority(IAuthorizationEvaluator evaluator, IOperationContextAccessor accessor)
{
    public async ValueTask<bool> AllowsAsync(string permission, Guid? site, CancellationToken cancellationToken)
    {
        if (accessor.Current is not { } operation)
        {
            return true;
        }

        var permissions = await evaluator.ResolveAsync(operation, cancellationToken);
        return permissions.Allows(permission, Target(site));
    }

    public async ValueTask RequireAsync(string permission, Guid? site, CancellationToken cancellationToken)
    {
        if (!await AllowsAsync(permission, site, cancellationToken))
        {
            throw new ManagementAccessDeniedException(site is { } one
                ? $"This needs the permission {permission} in the site {one:D}."
                : $"This needs the instance-wide permission {permission}.");
        }
    }

    // Whether the caller holds every grant given here, as it reaches through its role's site and the binding of a key.
    public async ValueTask<bool> CoversAsync(IEnumerable<(string Pattern, PermissionConstraints Constraints)> grants, Guid? roleSite, PrincipalBinding binding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grants);

        if (accessor.Current is not { } operation)
        {
            return true;
        }

        var permissions = await evaluator.ResolveAsync(operation, cancellationToken);
        return grants.SelectMany(grant => GrantReach.Of(grant.Pattern, grant.Constraints, roleSite, binding)).All(permissions.Covers);
    }

    public SiteId? CurrentSite => accessor.Current?.Site;

    private static AccessTarget Target(Guid? site) => new(site is { } one ? new SiteId(one) : null, null);
}
