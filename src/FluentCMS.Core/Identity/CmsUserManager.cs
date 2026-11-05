using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Core.Identity;

// Identity's user manager for admin users, with what the CMS adds: roles by id, so that a role of a site can be held too,
// deactivation and the forced password change.
public sealed class CmsUserManager : UserManager<CmsUser>
{
    private readonly TimeProvider _time;

    public CmsUserManager(
        IUserStore<CmsUser> store,
        IOptions<IdentityOptions> optionsAccessor,
        IPasswordHasher<CmsUser> passwordHasher,
        IEnumerable<IUserValidator<CmsUser>> userValidators,
        IEnumerable<IPasswordValidator<CmsUser>> passwordValidators,
        ILookupNormalizer keyNormalizer,
        IdentityErrorDescriber errors,
        IServiceProvider services,
        ILogger<UserManager<CmsUser>> logger,
        TimeProvider time)
        : base(store, optionsAccessor, passwordHasher, userValidators, passwordValidators, keyNormalizer, errors, services, logger)
    {
        _time = time;
    }

    private CmsUserStore RoleStore => Store as CmsUserStore
        ?? throw new NotSupportedException("Roles by id need the CMS user store.");

    public async Task<IdentityResult> AddToRolesAsync(CmsUser user, IEnumerable<Guid> roleIds)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(roleIds);

        List<Guid> ids = [.. roleIds.Distinct()];
        var held = await RoleStore.RoleIdsAsync(user, CancellationToken);
        if (ids.Find(held.Contains) is var holding && holding != Guid.Empty)
        {
            return IdentityResult.Failed(ErrorDescriber.UserAlreadyInRole(holding.ToString("D")));
        }

        if (await RoleStore.MissingRoleAsync(ids, CancellationToken) is { } missing)
        {
            return IdentityResult.Failed(new IdentityError { Code = IdentityErrorCodes.UnknownRole, Description = $"There is no role {missing:D}." });
        }

        foreach (var roleId in ids)
        {
            RoleStore.AddRole(user, roleId);
        }

        return await UpdateUserAsync(user);
    }

    public Task<IdentityResult> AddToRoleAsync(CmsUser user, Guid roleId) => AddToRolesAsync(user, [roleId]);

    public async Task<IdentityResult> RemoveFromRolesAsync(CmsUser user, IEnumerable<Guid> roleIds)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(roleIds);

        List<Guid> ids = [.. roleIds.Distinct()];
        var held = await RoleStore.RoleIdsAsync(user, CancellationToken);
        if (ids.Find(id => !held.Contains(id)) is var missing && missing != Guid.Empty)
        {
            return IdentityResult.Failed(ErrorDescriber.UserNotInRole(missing.ToString("D")));
        }

        foreach (var roleId in ids)
        {
            await RoleStore.RemoveRoleAsync(user, roleId, CancellationToken);
        }

        return await UpdateUserAsync(user);
    }

    public Task<IdentityResult> RemoveFromRoleAsync(CmsUser user, Guid roleId) => RemoveFromRolesAsync(user, [roleId]);

    public async Task<IReadOnlyList<Guid>> GetRoleIdsAsync(CmsUser user)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        return await RoleStore.RoleIdsAsync(user, CancellationToken);
    }

    // A new security stamp ends what the user holds wherever sign-in checks the stamp.
    public async Task<IdentityResult> DeactivateAsync(CmsUser user)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        if (user.DeactivatedAt is not null)
        {
            return IdentityResult.Success;
        }

        user.DeactivatedAt = _time.GetUtcNow();
        var result = await UpdateSecurityStampAsync(user);
        if (!result.Succeeded)
        {
            user.DeactivatedAt = null;
        }

        return result;
    }

    public async Task<IdentityResult> ReactivateAsync(CmsUser user)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        if (user.DeactivatedAt is not { } deactivated)
        {
            return IdentityResult.Success;
        }

        user.DeactivatedAt = null;
        var result = await UpdateUserAsync(user);
        if (!result.Succeeded)
        {
            user.DeactivatedAt = deactivated;
        }

        return result;
    }

    public async Task<IdentityResult> RequirePasswordChangeAsync(CmsUser user)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        if (user.MustChangePassword)
        {
            return IdentityResult.Success;
        }

        user.MustChangePassword = true;
        var result = await UpdateUserAsync(user);
        if (!result.Succeeded)
        {
            user.MustChangePassword = false;
        }

        return result;
    }

    public override async Task<bool> CheckPasswordAsync(CmsUser user, string password)
    {
        if (Store is not CmsUserStore store)
        {
            return await base.CheckPasswordAsync(user, password);
        }

        store.Rehashing = true;
        try
        {
            return await base.CheckPasswordAsync(user, password);
        }
        finally
        {
            store.Rehashing = false;
        }
    }

    public override Task<IdentityResult> ChangePasswordAsync(CmsUser user, string currentPassword, string newPassword) =>
        ClearingPasswordChangeAsync(user, () => base.ChangePasswordAsync(user, currentPassword, newPassword));

    public override Task<IdentityResult> ResetPasswordAsync(CmsUser user, string token, string newPassword) =>
        ClearingPasswordChangeAsync(user, () => base.ResetPasswordAsync(user, token, newPassword));

    // Every Identity call that stages rows of the user saves them here, so a failure here is where they are dropped.
    protected override async Task<IdentityResult> UpdateUserAsync(CmsUser user)
    {
        var result = await base.UpdateUserAsync(user);
        if (!result.Succeeded && Store is CmsUserStore store)
        {
            store.DiscardStaged(user);
        }

        return result;
    }

    // The user's own new password clears the flag, with its save, and the flag comes back if that fails. A password an admin
    // sets by removing and adding one keeps it, since that is how a temporary password is given.
    private static async Task<IdentityResult> ClearingPasswordChangeAsync(CmsUser user, Func<Task<IdentityResult>> change)
    {
        ArgumentNullException.ThrowIfNull(user);

        var required = user.MustChangePassword;
        user.MustChangePassword = false;
        var result = await change();
        if (!result.Succeeded)
        {
            user.MustChangePassword = required;
        }

        return result;
    }
}
