using FluentCMS.Activity;
using FluentCMS.Content;
using FluentCMS.Core.Events;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using FluentCMS.Providers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Authorization;

// Keeps at least one active user who holds `*` with no constraints through an instance-wide role, as Super Admin does, so
// that the instance can always be administered without editing the database (#2847, decision 5). A change that would leave
// none is refused whoever asks, an API key included, and the attempt is an activity row. The changes that can take the grant
// away run one at a time under a lock in the database, each counting the holders where its write cannot be overtaken, so
// two at once, on any provider or instance, cannot each count the other's holder and both commit. Adding one needs no lock.
internal sealed class SuperAdminGuard(
    IDistributedLock locks,
    IdentityChanges changes,
    SystemDbContext context,
    ITransactionProbe transaction,
    IActivityLog activity,
    IOperationContextAccessor accessor,
    TimeProvider time)
{
    public const string LockName = "fluentcms:super-admins";

    private const string LastHolder = "The change would leave no active user who holds every permission for the instance, such as through Super Admin; give the role to another user first.";

    // Long enough for a holder that stopped without releasing the lock to lose its lease, as for LockedKeyManager.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(45);

    public static bool IsSuperGrant(PermissionGrant grant) => grant.Permission == CorePermissions.All && grant.Constraints.IsEmpty;

    private static ValidationException Refusal() =>
        new([new ValidationError("id", IdentityErrorCodes.LastSuperAdmin, LastHolder)]);

    // A change of the user, such as deactivating it or taking roles from it, in one transaction of IdentityChanges. The
    // change is the attempt the activity log names when it is refused.
    public async Task<IdentityResult> ChangeUserAsync(CmsUser user, string change, Func<Task<IdentityResult>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(work);

        var refused = false;
        var result = await SerializedAsync(
            () => changes.RunAsync(
                async () =>
                {
                    var before = await AnyAsync(context, beside: null, cancellationToken);
                    var done = await work();
                    if (done.Succeeded && before && !await AnyAsync(context, beside: null, cancellationToken))
                    {
                        refused = true;
                        return IdentityResult.Failed(new IdentityError { Code = IdentityErrorCodes.LastSuperAdmin, Description = LastHolder });
                    }

                    return done;
                },
                cancellationToken),
            cancellationToken);

        if (refused)
        {
            await RefusedAsync("user", user.Id, user.UserName, change, cancellationToken);
        }

        return result;
    }

    // An edit that drops the grant from the role, saved by the caller on its own context. Under the lock no other change
    // takes the grant from anyone between the count and the save, so the role is refused while it is the only way left.
    public async Task ChangeRoleAsync(DbContext roles, Role role, Func<Task> save, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(save);

        var refused = await SerializedAsync(
            async () =>
            {
                if (await AnyAsync(roles, beside: null, cancellationToken) && !await AnyAsync(roles, beside: role.Id, cancellationToken))
                {
                    // What the edit staged must not reach a later save of the same scope.
                    roles.ChangeTracker.Clear();
                    return true;
                }

                await save();
                return false;
            },
            cancellationToken);

        if (refused)
        {
            await RefusedAsync("role", role.Id, role.Name, "update", cancellationToken);
            throw Refusal();
        }
    }

    // Whether an active user holds the grant, through a role other than the one beside, when one is given.
    private static Task<bool> AnyAsync(DbContext db, Guid? beside, CancellationToken cancellationToken) =>
        (from membership in db.Set<CmsUserRole>()
         join user in db.Set<CmsUser>() on membership.UserId equals user.Id
         where user.DeactivatedAt == null
             && membership.RoleId != beside
             && db.Set<Role>().Any(role => role.Id == membership.RoleId && role.SiteId == null)
             && db.Set<RoleGrant>().Any(grant => grant.RoleId == membership.RoleId && grant.Permission == CorePermissions.All && grant.Constraints == null)
         select user.Id).AnyAsync(cancellationToken);

    // The lock is taken before any transaction opens: on SQLite a transaction holds the write lock, so taking a lock row
    // from another connection inside one would wait on itself.
    private async Task<T> SerializedAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
    {
        if (transaction.InTransaction)
        {
            throw new InvalidOperationException($"{IdentityErrorCodes.WriteInTransaction}: a change that can take Super Admin away runs in a transaction of its own, so not inside another.");
        }

        await using var held = await locks.TryAcquireAsync(LockName, Wait, cancellationToken)
            ?? throw new TimeoutException($"The lock {LockName} was not free within {Wait.TotalSeconds:0} seconds, so the change was not made; try again.");
        return await work();
    }

    // Users and roles that are instance-wide belong to no site, as their other rows do.
    private ValueTask RefusedAsync(string targetType, Guid targetId, string? label, string change, CancellationToken cancellationToken)
    {
        var source = accessor.Current is { } operation
            ? EventSource.From(operation) with { SiteId = Guid.Empty }
            : EventSource.FromSystem(time.GetUtcNow());
        var entry = new ActivityEntry(targetType + ".change_refused")
        {
            TargetType = targetType,
            TargetId = targetId.ToString("D"),
            TargetLabel = label,
            Details = new Dictionary<string, string?> { ["change"] = change, ["code"] = IdentityErrorCodes.LastSuperAdmin },
        };

        return activity.WriteAsync(entry, source, CancellationToken.None);
    }
}
