using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Identity;

// Identity's EF store over the cms_user* tables, with the roles of cms_role. A role is named by Identity only among the
// instance-wide ones, because a name is unique only within its site; CmsUserManager reaches any role by id. A change is
// described from what was stored before it and raised as the identity events of the catalogue. Users are deactivated,
// never deleted: versions, schedules and the activity log name them.
internal sealed class CmsUserStore(SystemDbContext context, IdentityChanges changes, ILookupNormalizer normalizer, TimeProvider time, IdentityErrorDescriber? describer = null)
    : UserOnlyStore<CmsUser, SystemDbContext, Guid, IdentityUserClaim<Guid>, IdentityUserLogin<Guid>, IdentityUserToken<Guid>>(context, describer),
      IUserRoleStore<CmsUser>
{
    // The names under which UserStoreBase keeps them, which it does not expose.
    private const string InternalLoginProvider = "[AspNetUserStore]";
    private const string AuthenticatorKeyTokenName = "AuthenticatorKey";
    private const string RecoveryCodeTokenName = "RecoveryCodes";

    public override Task<IdentityResult> CreateAsync(CmsUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (user.CreatedAt == default)
        {
            user.CreatedAt = time.GetUtcNow();
        }

        return changes.RunAsync(
            async () =>
            {
                var (added, removed) = RoleChanges(user);
                var logins = LoginChanges(user);
                var result = await base.CreateAsync(user, cancellationToken);
                if (result.Succeeded)
                {
                    await changes.RaiseAsync(new UserCreatedEvent(user.Id, user.UserName ?? ""), cancellationToken);
                    await RaiseRolesAsync(user, added, removed, cancellationToken);
                    await RaiseLoginsAsync(user, logins, cancellationToken);
                }

                return result;
            },
            cancellationToken);
    }

    public override async Task<IdentityResult> UpdateAsync(CmsUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        var before = await StoredAsync(user, cancellationToken);
        return await changes.RunAsync(
            async () =>
            {
                var (added, removed) = RoleChanges(user);
                var logins = LoginChanges(user);
                var result = await base.UpdateAsync(user, cancellationToken);
                if (!result.Succeeded || before is null)
                {
                    return result;
                }

                if (before.DeactivatedAt is null && user.DeactivatedAt is not null)
                {
                    // What a user sent out must not outlive its right to send it.
                    await Context.Set<Invitation>()
                        .Where(row => row.CreatedBy == user.Id && row.AcceptedAt == null && row.RevokedAt == null)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RevokedAt, user.DeactivatedAt), cancellationToken);
                    await changes.RaiseAsync(new UserDeactivatedEvent(user.Id, user.UserName ?? ""), cancellationToken);
                }

                if (Changes(before, user) is { Count: > 0 } changed)
                {
                    await changes.RaiseAsync(new UserUpdatedEvent(user.Id, user.UserName ?? "", changed), cancellationToken);
                }

                await RaiseRolesAsync(user, added, removed, cancellationToken);
                await RaiseLoginsAsync(user, logins, cancellationToken);
                return result;
            },
            cancellationToken);
    }

    public override Task<IdentityResult> DeleteAsync(CmsUser user, CancellationToken cancellationToken = default) =>
        Task.FromResult(IdentityResult.Failed(new IdentityError
        {
            Code = IdentityErrorCodes.DeleteNotSupported,
            Description = "An admin user is deactivated, not deleted, because versions, schedules and the activity log name it.",
        }));

    public async Task AddToRoleAsync(CmsUser user, string normalizedRoleName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var role = await InstanceRoleAsync(normalizedRoleName, cancellationToken)
            ?? throw new InvalidOperationException($"{IdentityErrorCodes.UnknownRole}: there is no instance-wide role named '{normalizedRoleName}'.");
        AddRole(user, role.Id);
    }

    public async Task RemoveFromRoleAsync(CmsUser user, string normalizedRoleName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (await InstanceRoleAsync(normalizedRoleName, cancellationToken) is { } role)
        {
            await RemoveRoleAsync(user, role.Id, cancellationToken);
        }
    }

    public async Task<IList<string>> GetRolesAsync(CmsUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        // Instance-wide only, as for IsInRoleAsync: a site role may carry the name of an instance-wide one, such as Super Admin.
        return await Context.Set<CmsUserRole>().AsNoTracking()
            .Where(membership => membership.UserId == user.Id)
            .Join(Context.Set<Role>().Where(role => role.SiteId == null), membership => membership.RoleId, role => role.Id, (_, role) => role.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> IsInRoleAsync(CmsUser user, string normalizedRoleName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        return await InstanceRoleAsync(normalizedRoleName, cancellationToken) is { } role
            && (await RoleIdsAsync(user, cancellationToken)).Contains(role.Id);
    }

    public async Task<IList<CmsUser>> GetUsersInRoleAsync(string normalizedRoleName, CancellationToken cancellationToken)
    {
        if (await InstanceRoleAsync(normalizedRoleName, cancellationToken) is not { } role)
        {
            return [];
        }

        return await Context.Set<CmsUserRole>().AsNoTracking()
            .Where(membership => membership.RoleId == role.Id)
            .Join(Context.Set<CmsUser>(), membership => membership.UserId, user => user.Id, (_, user) => user)
            .ToListAsync(cancellationToken);
    }

    public async Task<Guid?> MissingRoleAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
    {
        var found = await Context.Set<Role>().AsNoTracking().Where(role => roleIds.Contains(role.Id)).Select(role => role.Id).ToListAsync(cancellationToken);
        return roleIds.Where(id => !found.Contains(id)).Select(id => (Guid?)id).FirstOrDefault();
    }

    // Staged like the rest of Identity's changes, for UpdateAsync to save. The role must exist and the user not hold it.
    public void AddRole(CmsUser user, Guid roleId)
    {
        var held = Context.ChangeTracker.Entries<CmsUserRole>().FirstOrDefault(entry => entry.Entity.UserId == user.Id && entry.Entity.RoleId == roleId);
        if (held is { State: EntityState.Deleted })
        {
            held.State = EntityState.Unchanged;
            return;
        }

        Context.Add(new CmsUserRole { UserId = user.Id, RoleId = roleId });
    }

    // A call that failed after it staged rows of the user leaves them for no one: the next save would commit them.
    public void DiscardStaged(CmsUser user)
    {
        var staged = Context.ChangeTracker.Entries()
            .Where(entry => (entry.State is EntityState.Added or EntityState.Deleted or EntityState.Modified) && OwnerOf(entry.Entity) == user.Id)
            .ToList();
        foreach (var entry in staged)
        {
            if (entry.State == EntityState.Added)
            {
                entry.State = EntityState.Detached;
            }
            else
            {
                entry.CurrentValues.SetValues(entry.OriginalValues);
                entry.State = EntityState.Unchanged;
            }
        }
    }

    public async Task RemoveRoleAsync(CmsUser user, Guid roleId, CancellationToken cancellationToken)
    {
        if (await Context.Set<CmsUserRole>().FindAsync([user.Id, roleId], cancellationToken) is { } held)
        {
            Context.Remove(held);
        }
    }

    // Staged for the caller's save: the rows in which Identity's store keeps the authenticator key and the recovery codes.
    public async Task ForgetTwoFactorAsync(CmsUser user, CancellationToken cancellationToken)
    {
        await RemoveTokenAsync(user, InternalLoginProvider, AuthenticatorKeyTokenName, cancellationToken);
        await RemoveTokenAsync(user, InternalLoginProvider, RecoveryCodeTokenName, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> RoleIdsAsync(CmsUser user, CancellationToken cancellationToken) =>
        await Context.Set<CmsUserRole>().AsNoTracking()
            .Where(membership => membership.UserId == user.Id)
            .Select(membership => membership.RoleId)
            .ToListAsync(cancellationToken);

    private static Guid? OwnerOf(object entity) => entity switch
    {
        CmsUserRole membership => membership.UserId,
        IdentityUserClaim<Guid> claim => claim.UserId,
        IdentityUserLogin<Guid> login => login.UserId,
        IdentityUserToken<Guid> token => token.UserId,
        _ => null,
    };

    private async Task<Role?> InstanceRoleAsync(string normalizedRoleName, CancellationToken cancellationToken)
    {
        var roles = await Context.Set<Role>().AsNoTracking().Where(role => role.SiteId == null).ToListAsync(cancellationToken);
        return roles.Find(role => normalizer.NormalizeName(role.Name) == normalizedRoleName);
    }

    // The user as it is stored. A tracked user keeps what was read; one the caller built is read again.
    private async Task<CmsUser?> StoredAsync(CmsUser user, CancellationToken cancellationToken)
    {
        var entry = Context.Entry(user);
        if (entry.State != EntityState.Detached)
        {
            return (CmsUser)entry.OriginalValues.ToObject();
        }

        return await Context.Set<CmsUser>().AsNoTracking().FirstOrDefaultAsync(stored => stored.Id == user.Id, cancellationToken);
    }

    private (List<Guid> Added, List<Guid> Removed) RoleChanges(CmsUser user)
    {
        var entries = Context.ChangeTracker.Entries<CmsUserRole>().Where(entry => entry.Entity.UserId == user.Id).ToList();
        return (
            [.. entries.Where(entry => entry.State == EntityState.Added).Select(entry => entry.Entity.RoleId)],
            [.. entries.Where(entry => entry.State == EntityState.Deleted).Select(entry => entry.Entity.RoleId)]);
    }

    private List<(string Provider, bool Linked)> LoginChanges(CmsUser user) =>
        [.. Context.ChangeTracker.Entries<IdentityUserLogin<Guid>>()
            .Where(entry => entry.Entity.UserId == user.Id && entry.State is EntityState.Added or EntityState.Deleted)
            .Select(entry => (entry.Entity.LoginProvider, entry.State == EntityState.Added))];

    private async Task RaiseLoginsAsync(CmsUser user, List<(string Provider, bool Linked)> logins, CancellationToken cancellationToken)
    {
        foreach (var (provider, linked) in logins)
        {
            await changes.RaiseAsync(
                linked ? new UserLoginLinkedEvent(user.Id, user.UserName ?? "", provider) : new UserLoginUnlinkedEvent(user.Id, user.UserName ?? "", provider),
                cancellationToken);
        }
    }

    private async Task RaiseRolesAsync(CmsUser user, List<Guid> added, List<Guid> removed, CancellationToken cancellationToken)
    {
        if (added.Count > 0 || removed.Count > 0)
        {
            await changes.RaiseAsync(new UserRoleChangedEvent(user.Id, user.UserName ?? "", added, removed), cancellationToken);
        }
    }

    // Set by CmsUserManager while a password is checked: a check that rehashes the password saves it again, which changes
    // its hash and its security stamp like a new password would.
    internal bool Rehashing { get; set; }

    // What an admin would call a change. The stamps, the normalized copies and the counters of failed sign-ins are not.
    private List<string> Changes(CmsUser before, CmsUser after)
    {
        List<string> changed = [];
        Add(UserChanges.UserName, before.UserName != after.UserName);
        Add(UserChanges.Email, before.Email != after.Email);
        Add(UserChanges.EmailConfirmed, before.EmailConfirmed != after.EmailConfirmed);
        Add(UserChanges.PhoneNumber, before.PhoneNumber != after.PhoneNumber);
        Add(UserChanges.Password, !Rehashing && before.PasswordHash != after.PasswordHash);
        Add(UserChanges.TwoFactor, before.TwoFactorEnabled != after.TwoFactorEnabled);
        Add(UserChanges.LockoutEnabled, before.LockoutEnabled != after.LockoutEnabled);
        Add(UserChanges.MustChangePassword, before.MustChangePassword != after.MustChangePassword);
        Add(UserChanges.Reactivated, before.DeactivatedAt is not null && after.DeactivatedAt is null);
        return changed;

        void Add(string name, bool differs)
        {
            if (differs)
            {
                changed.Add(name);
            }
        }
    }
}
