using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Core.Identity;

// Identity's lockout on the host's TimeProvider. In .NET 10 UserManager's IsLockedOutAsync and AccessFailedAsync read
// DateTimeOffset.UtcNow and take no TimeProvider, so the managers of both pools override them with these, which keep
// Identity's rules: a user is locked out until the end and at it, and the failure that reaches the limit sets the end and
// starts the count again. The sign-in managers reach the lockout through those two methods only.
internal static class ClockedLockout
{
    public static async Task<bool> IsLockedOutAsync<TUser>(IUserLockoutStore<TUser> store, TUser user, TimeProvider time, CancellationToken cancellationToken)
        where TUser : class =>
        await store.GetLockoutEnabledAsync(user, cancellationToken)
        && await store.GetLockoutEndDateAsync(user, cancellationToken) >= time.GetUtcNow();

    // The caller saves the user.
    public static async Task CountFailureAsync<TUser>(IUserLockoutStore<TUser> store, TUser user, LockoutOptions options, TimeProvider time, CancellationToken cancellationToken)
        where TUser : class
    {
        if (await store.IncrementAccessFailedCountAsync(user, cancellationToken) < options.MaxFailedAccessAttempts)
        {
            return;
        }

        await store.SetLockoutEndDateAsync(user, time.GetUtcNow() + options.DefaultLockoutTimeSpan, cancellationToken);
        await store.ResetAccessFailedCountAsync(user, cancellationToken);
    }
}
