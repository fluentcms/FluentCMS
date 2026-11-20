using System.Globalization;
using FluentCMS.Core.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Core.Identity;

// Identity's user manager for admin users, with what the CMS adds: roles by id, so that a role of a site can be held too,
// deactivation, the forced password change, and the lockout on the host's clock.
public sealed class CmsUserManager : UserManager<CmsUser>
{
    // The code of a change refused while the user is locked out, which only the hosted page shows, so no FCMS code.
    internal const string LockedOutCode = "UserLockedOut";

    // UserStoreBase keeps the authenticator key under this provider name too.
    private const string StoreProvider = "[AspNetUserStore]";
    private const string SignedInFailuresName = "SignedInFailures";

    private readonly TimeProvider _time;
    private readonly IServiceProvider _services;

    // The user a failed save left with what is stored, which is newer than what the caller remembers of it.
    private CmsUser? _reloaded;

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
        _services = services;
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

    // A key that waits for a code of it to turn two-factor on; TwoFactorService sets one only while two-factor is off. Until
    // then the user signs in as before, so the security stamp stays and the user's sessions go on, which Identity's
    // ResetAuthenticatorKeyAsync would end.
    public async Task<IdentityResult> SetPendingAuthenticatorKeyAsync(CmsUser user)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        await RoleStore.SetAuthenticatorKeyAsync(user, GenerateNewAuthenticatorKey(), CancellationToken);
        return await UpdateUserAsync(user);
    }

    // Two-factor off, without its key and recovery codes, in one save with a new security stamp, which ends what the user
    // signed in to with the second factor.
    public async Task<IdentityResult> DisableTwoFactorAsync(CmsUser user)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        await RoleStore.ForgetTwoFactorAsync(user, CancellationToken);
        await RoleStore.SetTwoFactorEnabledAsync(user, false, CancellationToken);
        return await UpdateSecurityStampAsync(user);
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

    // A store without lockout is left to Identity, which refuses it.
    public override async Task<bool> IsLockedOutAsync(CmsUser user)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        return Store is IUserLockoutStore<CmsUser> store
            ? await ClockedLockout.IsLockedOutAsync(store, user, _time, CancellationToken)
            : await base.IsLockedOutAsync(user);
    }

    public override async Task<IdentityResult> AccessFailedAsync(CmsUser user)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        if (Store is not IUserLockoutStore<CmsUser> store)
        {
            return await base.AccessFailedAsync(user);
        }

        await ClockedLockout.CountFailureAsync(store, user, Options.Lockout, _time, CancellationToken);
        return await UpdateUserAsync(user);
    }

    // Anyone who knows a user name raises the lockout with wrong passwords, so it holds for a new sign-in only and not for
    // what a user who is signed in already has to do: change the password, enrol an authenticator (#3371). What a session
    // gets wrong is bounded by a count of its own, with the lockout's limit and time, kept with the user as the token
    // SignedInFailures ("count:lockedUntil", in Unix seconds) and raised only by requests that carry the user's sign-in.
    public async Task<bool> IsSignedInLockedOutAsync(CmsUser user)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        return SupportsUserLockout && await GetLockoutEnabledAsync(user) && (await SignedInFailuresAsync(user)).Until >= _time.GetUtcNow();
    }

    // A wrong password or code of a signed-in user counts towards both, the lockout as before, in one save.
    public async Task<IdentityResult> SignedInAccessFailedAsync(CmsUser user)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        if (Store is IUserAuthenticationTokenStore<CmsUser> tokens)
        {
            var (count, until) = await SignedInFailuresAsync(user);
            if (++count >= Options.Lockout.MaxFailedAccessAttempts)
            {
                (count, until) = (0, _time.GetUtcNow() + Options.Lockout.DefaultLockoutTimeSpan);
            }

            var value = string.Create(CultureInfo.InvariantCulture, $"{count}:{until?.ToUnixTimeSeconds()}");
            await tokens.SetTokenAsync(user, StoreProvider, SignedInFailuresName, value, CancellationToken);
        }

        return await AccessFailedAsync(user);
    }

    // A right password or code of a signed-in user starts both counts again.
    public async Task<IdentityResult> ResetSignedInFailuresAsync(CmsUser user)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        if (Store is not IUserAuthenticationTokenStore<CmsUser> tokens || await tokens.GetTokenAsync(user, StoreProvider, SignedInFailuresName, CancellationToken) is null)
        {
            return await ResetAccessFailedCountAsync(user);
        }

        await tokens.RemoveTokenAsync(user, StoreProvider, SignedInFailuresName, CancellationToken);
        user.AccessFailedCount = 0;
        return await UpdateUserAsync(user);
    }

    // A wrong current password counts towards the lockout, as at sign-in, and towards the count of the user's sessions, so
    // that a session that leaked is no way to guess it; while that count keeps the sessions out the change is refused
    // unchecked, and a right one starts the counts again. A user with no password, one of a provider only, has nothing to
    // guess, so nothing is counted.
    public override async Task<IdentityResult> ChangePasswordAsync(CmsUser user, string currentPassword, string newPassword)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        if (await IsSignedInLockedOutAsync(user))
        {
            return IdentityResult.Failed(new IdentityError { Code = LockedOutCode, Description = "The account is locked out for now; try again later." });
        }

        // The count starts again in the save of the new password, and comes back if that fails, unless a failed save
        // reloaded the user with what is stored now, which another request may have changed since.
        var failures = user.AccessFailedCount;
        user.AccessFailedCount = 0;
        _reloaded = null;
        var result = await ClearingPasswordChangeAsync(user, () => base.ChangePasswordAsync(user, currentPassword, newPassword));
        if (result.Succeeded)
        {
            // Its failure leaves the sessions' count as it was, which only keeps them out sooner.
            await ResetSignedInFailuresAsync(user);
            return result;
        }

        if (!ReferenceEquals(_reloaded, user))
        {
            user.AccessFailedCount = failures;
        }

        if (SupportsUserLockout && result.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.PasswordMismatch)) && await HasPasswordAsync(user))
        {
            await SignedInAccessFailedAsync(user);
        }

        return result;
    }

    // A password an operator sets, with a new security stamp in the same save, and the forced change, since the user did not
    // choose it.
    public async Task<IdentityResult> SetPasswordAsync(CmsUser user, string newPassword)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(newPassword);

        var required = user.MustChangePassword;
        user.MustChangePassword = true;
        var result = await UpdatePasswordHash(user, newPassword, validatePassword: true);
        result = result.Succeeded ? await UpdateUserAsync(user) : result;
        if (!result.Succeeded)
        {
            user.MustChangePassword = required;
        }

        return result;
    }

    public override Task<IdentityResult> ResetPasswordAsync(CmsUser user, string token, string newPassword) =>
        ClearingPasswordChangeAsync(user, () => base.ResetPasswordAsync(user, token, newPassword));

    // Every Identity call that stages rows of the user saves them here, so a failure here is where they are dropped. Two
    // saves that add the same token row of the user at once, as two requests taking the first authenticator code a user
    // gives do, meet on its key unless the user's concurrency stamp stops the second first: either way it is a concurrency
    // failure, whatever order the commands run in. Any other unique key the save breaks is not this race, and is thrown.
    protected override async Task<IdentityResult> UpdateUserAsync(CmsUser user)
    {
        var store = Store as CmsUserStore;
        var addsToken = store?.StagesTokenRow(user) == true;
        IdentityResult result;
        try
        {
            result = await base.UpdateUserAsync(user);
        }
        catch (DbUpdateException exception) when (addsToken && _services.GetService<IDataProvider>()?.IsUniqueViolation(exception) == true && store!.IsTokenKeyViolation(exception))
        {
            result = IdentityResult.Failed(ErrorDescriber.ConcurrencyFailure());
        }

        if (!result.Succeeded && store is not null)
        {
            store.DiscardStaged(user);
            if (await store.ReattachAsync(user, CancellationToken))
            {
                _reloaded = user;
            }
        }

        return result;
    }

    private async Task<(int Count, DateTimeOffset? Until)> SignedInFailuresAsync(CmsUser user)
    {
        var parts = (await GetAuthenticationTokenAsync(user, StoreProvider, SignedInFailuresName))?.Split(':');
        return parts is [var count, var until] && int.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out var failures)
            ? (failures, long.TryParse(until, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null)
            : (0, null);
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
