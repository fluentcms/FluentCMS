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

    // The wrong current passwords of the user's sessions, and their wrong codes of two-factor, each "count:lockedUntil".
    private const string PasswordFailuresName = "SignedInFailures";
    private const string CodeFailuresName = "SignedInCodeFailures";

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
        _reloaded = null;
        var result = await UpdateSecurityStampAsync(user);
        if (!result.Succeeded && !Reloaded(user))
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
        _reloaded = null;
        var result = await UpdateUserAsync(user);
        if (!result.Succeeded && !Reloaded(user))
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
        _reloaded = null;
        var result = await UpdateUserAsync(user);
        if (!result.Succeeded && !Reloaded(user))
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
    // what a user who is signed in already has to do: change the password, enrol an authenticator (#3371). What the user's
    // sessions get wrong is bounded by counts of their own, with the lockout's limit and time, raised only by requests that
    // carry the user's sign-in: wrong current passwords and wrong codes of two-factor, apart, as the tokens SignedInFailures
    // and SignedInCodeFailures ("count:lockedUntil", in Unix seconds). Either at its limit keeps the sessions out of both, and
    // each starts again only with a right one of its own kind, so that a right code is no way to guess more passwords, nor
    // a right password to guess more codes (#3447).
    public async Task<bool> IsSignedInLockedOutAsync(CmsUser user)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        if (!SupportsUserLockout || !await GetLockoutEnabledAsync(user))
        {
            return false;
        }

        var now = _time.GetUtcNow();
        return (await SignedInFailuresAsync(user, PasswordFailuresName)).Until >= now || (await SignedInFailuresAsync(user, CodeFailuresName)).Until >= now;
    }

    // A code takes its place in the count of codes before it is checked (TakePlaceAsync).
    internal Task<IdentityResult> TakeCodePlaceAsync(CmsUser user) => TakePlaceAsync(user, CodeFailuresName);

    // A right code gives its place back and starts the count of codes again. It proves the second factor and not the
    // password, so the count of wrong passwords and the lockout stay.
    internal Task<IdentityResult> ResetSignedInCodeFailuresAsync(CmsUser user) => ResetSignedInFailuresAsync(user, CodeFailuresName);

    // A wrong password or code of a signed-in user, once checked, counts towards the lockout too, as a wrong one at sign-in
    // does. Its place in the sessions' count is already saved, so a failure here leaves only the lockout's count short.
    internal async Task CountFailedSignInAsync(CmsUser user)
    {
        if (SupportsUserLockout)
        {
            await RetryingAsync(user, () => AccessFailedAsync(user));
        }
    }

    // A wrong current password counts towards the count of the user's sessions and the lockout, so that a session that
    // leaked is no way to guess it, and a right one starts both again. A user with no password, one of a provider only, has
    // nothing to guess, so nothing is counted. The new password is checked against the policy first, so that what the
    // policy answers never depends on the current one, and the current one is checked only once it holds a place in the
    // count (#3447). A right one then takes the new password in a save that gives its place back; one whose save keeps losing
    // to other saves of the user is answered as a wrong one would be, and its place stays counted.
    public override async Task<IdentityResult> ChangePasswordAsync(CmsUser user, string currentPassword, string newPassword)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        var passwords = Store as IUserPasswordStore<CmsUser> ?? throw new NotSupportedException("A password change needs a store of passwords.");
        var valid = await ValidatePasswordAsync(user, newPassword);
        if (!valid.Succeeded)
        {
            return valid;
        }

        if (!await HasPasswordAsync(user))
        {
            return IdentityResult.Failed(ErrorDescriber.PasswordMismatch());
        }

        var place = await TakePlaceAsync(user, PasswordFailuresName);
        if (!place.Succeeded)
        {
            return place;
        }

        for (var attempt = 0; attempt < SaveAttempts; attempt++)
        {
            if (await VerifyPasswordAsync(passwords, user, currentPassword) == PasswordVerificationResult.Failed)
            {
                await CountFailedSignInAsync(user);
                return IdentityResult.Failed(ErrorDescriber.PasswordMismatch());
            }

            var result = await SaveNewPasswordAsync(user, newPassword);
            if (result.Succeeded || !Lost(result, user))
            {
                return result;
            }
        }

        return IdentityResult.Failed(ErrorDescriber.PasswordMismatch());
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
        _reloaded = null;
        var result = await UpdatePasswordHash(user, newPassword, validatePassword: true);
        result = result.Succeeded ? await UpdateUserAsync(user) : result;
        if (!result.Succeeded && !Reloaded(user))
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
        catch (DbUpdateException exception) when (addsToken && _services.GetService<IDataProvider>() is { } provider && store!.IsTokenKeyViolation(provider, exception))
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

    // Each lost save is a save of the user by another request that went through, so the tries run out only while more of them
    // go through than the counts of the sessions and the lockout let pass before they refuse.
    private int SaveAttempts => 2 * Math.Max(Options.Lockout.MaxFailedAccessAttempts, 1);

    // A guess of a signed-in user takes its place in its count, in a save that goes through, before it is checked, so that
    // no more guesses are checked in the lockout's time than its limit, however many are sent at once, and an answer that
    // depends on a guess never comes before the save that counted it (#3447). The place that reaches the limit keeps the
    // sessions out from then on: a request that finds them kept out is refused with its guess unchecked. A save that loses
    // to another save of the user is made again on what is stored then; one whose tries run out answers the concurrency
    // failure, with its guess unchecked too.
    private Task<IdentityResult> TakePlaceAsync(CmsUser user, string name) => RetryingAsync(user, async () =>
    {
        if (await IsSignedInLockedOutAsync(user))
        {
            return IdentityResult.Failed(new IdentityError { Code = LockedOutCode, Description = "The account is locked out for now; try again later." });
        }

        if (!SupportsUserLockout || Store is not IUserAuthenticationTokenStore<CmsUser> tokens)
        {
            return IdentityResult.Success;
        }

        var (count, until) = await SignedInFailuresAsync(user, name);
        if (++count >= Options.Lockout.MaxFailedAccessAttempts)
        {
            (count, until) = (0, _time.GetUtcNow() + Options.Lockout.DefaultLockoutTimeSpan);
        }

        var value = string.Create(CultureInfo.InvariantCulture, $"{count}:{until?.ToUnixTimeSeconds()}");
        await tokens.SetTokenAsync(user, StoreProvider, name, value, CancellationToken);
        return await UpdateUserAsync(user);
    });

    // The new password, the flag cleared, and the count of passwords and the lockout's started again, the guess's own place
    // included, in one save. The lockout's count comes back if it fails, unless the failed save reloaded the user with what
    // is stored now, which another request may have changed since.
    private async Task<IdentityResult> SaveNewPasswordAsync(CmsUser user, string newPassword)
    {
        var failures = user.AccessFailedCount;
        user.AccessFailedCount = 0;
        var result = await ClearingPasswordChangeAsync(user, async () =>
        {
            var hashed = await UpdatePasswordHash(user, newPassword, validatePassword: false);
            if (!hashed.Succeeded)
            {
                return hashed;
            }

            if (Store is IUserAuthenticationTokenStore<CmsUser> tokens && await tokens.GetTokenAsync(user, StoreProvider, PasswordFailuresName, CancellationToken) is not null)
            {
                await tokens.RemoveTokenAsync(user, StoreProvider, PasswordFailuresName, CancellationToken);
            }

            return await UpdateUserAsync(user);
        });

        if (!result.Succeeded && !Reloaded(user))
        {
            user.AccessFailedCount = failures;
        }

        return result;
    }

    private async Task<IdentityResult> RetryingAsync(CmsUser user, Func<Task<IdentityResult>> save)
    {
        var attempts = SaveAttempts;
        IdentityResult result;
        do
        {
            _reloaded = null;
            result = await save();
        }
        while (--attempts > 0 && Lost(result, user));

        return result;
    }

    // Whether the save lost to another save of the user and reloaded it, so that it can be made again on what is stored.
    private bool Lost(IdentityResult result, CmsUser user) =>
        Reloaded(user) && result.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure));

    private async Task<IdentityResult> ResetSignedInFailuresAsync(CmsUser user, string name)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        if (Store is not IUserAuthenticationTokenStore<CmsUser> tokens || await tokens.GetTokenAsync(user, StoreProvider, name, CancellationToken) is null)
        {
            return IdentityResult.Success;
        }

        await tokens.RemoveTokenAsync(user, StoreProvider, name, CancellationToken);
        return await UpdateUserAsync(user);
    }

    private async Task<(int Count, DateTimeOffset? Until)> SignedInFailuresAsync(CmsUser user, string name)
    {
        var parts = (await GetAuthenticationTokenAsync(user, StoreProvider, name))?.Split(':');
        return parts is [var count, var until] && int.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out var failures)
            ? (failures, long.TryParse(until, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null)
            : (0, null);
    }

    // The user's own new password clears the flag, with its save, and the flag comes back if that fails, unless a failed save
    // reloaded the user with what is stored now (#3447). A password an admin sets by removing and adding one keeps it, since
    // that is how a temporary password is given.
    private async Task<IdentityResult> ClearingPasswordChangeAsync(CmsUser user, Func<Task<IdentityResult>> change)
    {
        ArgumentNullException.ThrowIfNull(user);

        var required = user.MustChangePassword;
        user.MustChangePassword = false;
        _reloaded = null;
        var result = await change();
        if (!result.Succeeded && !Reloaded(user))
        {
            user.MustChangePassword = required;
        }

        return result;
    }

    // Whether a failed save of the call reloaded the user with what is stored, which a value it changed is then not put back
    // over: that may be another request's by now, and a later save of the scope would store the stale one (#3381, #3447).
    private bool Reloaded(CmsUser user) => ReferenceEquals(_reloaded, user);
}
