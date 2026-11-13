using FluentCMS.Content;
using FluentCMS.Sites;

namespace FluentCMS.Core.Authorization;

// The Management API's refusal of a principal without the permission a call needs: 403, or 401 when it is not signed in.
internal sealed class ManagementAccessDeniedException(string message) : Exception(message);

// The permission checks of the services the Management API calls: users, roles, keys, sites, jobs and the activity log.
// A null site is the instance as a whole, which only a grant with no site limit reaches. Without an operation nothing is
// allowed: host code that means to act as the system says so with SystemOperation.Begin, the one way to skip the checks.
internal sealed class ManagementAuthority(IAuthorizationEvaluator evaluator, IOperationContextAccessor accessor)
{
    public async ValueTask<bool> AllowsAsync(string permission, Guid? site, CancellationToken cancellationToken)
    {
        var permissions = await evaluator.ResolveAsync(Operation(), cancellationToken);
        return permissions.Allows(permission, Target(site));
    }

    public async ValueTask<IReadOnlySet<string>?> ResourcesAsync(string permission, Guid? site, CancellationToken cancellationToken)
    {
        var permissions = await evaluator.ResolveAsync(Operation(), cancellationToken);
        return permissions.ResourcesOf(permission, Target(site));
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

    public async ValueTask<bool> CanManageAsync(CancellationToken cancellationToken)
    {
        var permissions = await evaluator.ResolveAsync(Operation(), cancellationToken);
        return permissions.CanManage;
    }

    // Whether any grant of the caller reaches the site, whatever it permits there.
    public async ValueTask<bool> HoldsGrantInAsync(Guid site, CancellationToken cancellationToken)
    {
        var permissions = await evaluator.ResolveAsync(Operation(), cancellationToken);
        return permissions.HoldsGrantIn(new SiteId(site));
    }

    // Whether the caller holds every grant given here, as it reaches through its role's site and the binding of a key.
    public async ValueTask<bool> CoversAsync(IEnumerable<(string Pattern, PermissionConstraints Constraints)> grants, Guid? roleSite, PrincipalBinding binding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grants);

        var permissions = await evaluator.ResolveAsync(Operation(), cancellationToken);
        return grants.SelectMany(grant => GrantReach.Of(grant.Pattern, grant.Constraints, roleSite, binding)).All(permissions.Covers);
    }

    public SiteId? CurrentSite => accessor.Current?.Site;

    // Thrown rather than answered false, so that code which forgot its operation fails where it runs and not as a 403.
    private OperationContext Operation() => accessor.Current ?? throw new InvalidOperationException(
        $"{AuthorizationErrorCodes.NoOperation}: a permission was checked with no current operation. Host code, a job or a seed that acts as the system runs inside SystemOperation.Begin.");

    private static AccessTarget Target(Guid? site) => new(site is { } one ? new SiteId(one) : null, null);
}
