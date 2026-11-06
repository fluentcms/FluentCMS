using FluentCMS.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Identity;

internal sealed record TwoFactorStatus(bool Enabled, bool Required, bool Pending, int RecoveryCodesLeft);

internal sealed record AuthenticatorSetup(string SharedKey, string AuthenticatorUri);

// Two-factor sign-in of an admin user with an authenticator app (TOTP, Identity's authenticator provider) and recovery codes.
// A user enrols a key, which waits until a code of it turns two-factor on and brings the recovery codes. Turning it off or
// replacing the codes takes a code too, so that a stolen token alone cannot undo the second factor. A role flagged
// RequireTwoFactor makes the authorize endpoint ask for it.
internal sealed class TwoFactorService(CmsUserManager users, SystemDbContext context)
{
    public const int RecoveryCodeCount = 10;

    public async Task<bool> IsRequiredAsync(Guid userId, CancellationToken cancellationToken) =>
        await context.Set<CmsUserRole>().AsNoTracking()
            .Where(membership => membership.UserId == userId)
            .Join(context.Set<Role>(), membership => membership.RoleId, role => role.Id, (_, role) => role.RequireTwoFactor)
            .AnyAsync(required => required, cancellationToken);

    // On with a key to check codes against, which is when Identity's sign-in asks for the second factor. The flag without a
    // key counts as off, so that such a user is sent to enrol rather than round the sign-in again.
    public async Task<bool> IsOnAsync(CmsUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return await users.GetTwoFactorEnabledAsync(user) && !string.IsNullOrEmpty(await users.GetAuthenticatorKeyAsync(user));
    }

    public async Task<TwoFactorStatus> StatusAsync(CmsUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var enabled = await IsOnAsync(user);
        var pending = !enabled && !string.IsNullOrEmpty(await users.GetAuthenticatorKeyAsync(user));
        var left = enabled ? await users.CountRecoveryCodesAsync(user) : 0;
        return new TwoFactorStatus(enabled, await IsRequiredAsync(user.Id, cancellationToken), pending, left);
    }

    // A new key, which replaces one that waits.
    public async Task<AuthenticatorSetup> EnrolAsync(CmsUser user, string issuer)
    {
        await RequireOffAsync(user);
        UserViews.Check(await users.SetPendingAuthenticatorKeyAsync(user));
        return Setup(user, (await users.GetAuthenticatorKeyAsync(user))!, issuer);
    }

    // The key that waits, or a new one, so that a page opened again shows the key the app already has.
    public async Task<AuthenticatorSetup> PendingAsync(CmsUser user, string issuer)
    {
        await RequireOffAsync(user);
        if (string.IsNullOrEmpty(await users.GetAuthenticatorKeyAsync(user)))
        {
            UserViews.Check(await users.SetPendingAuthenticatorKeyAsync(user));
        }

        return Setup(user, (await users.GetAuthenticatorKeyAsync(user))!, issuer);
    }

    // A code of the key that waits turns two-factor on, with a new security stamp, and returns the recovery codes, which are
    // shown once.
    public async Task<IReadOnlyList<string>> VerifyAsync(CmsUser user, string? code)
    {
        await RequireOffAsync(user);
        if (string.IsNullOrEmpty(await users.GetAuthenticatorKeyAsync(user)))
        {
            throw new ValidationException([new ValidationError("code", IdentityErrorCodes.NoAuthenticator, "There is no authenticator to verify; enrol one first.")]);
        }

        await RequireCodeAsync(user, code, recoveryCode: false);
        var codes = await NewRecoveryCodesAsync(user);
        UserViews.Check(await users.SetTwoFactorEnabledAsync(user, true));
        return codes;
    }

    public async Task<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(CmsUser user, string? code)
    {
        await RequireOnAsync(user);
        await RequireCodeAsync(user, code, recoveryCode: false);
        return await NewRecoveryCodesAsync(user);
    }

    // A recovery code will do, for a user who lost the app and signed in with one.
    public async Task DisableAsync(CmsUser user, string? code)
    {
        await RequireOnAsync(user);
        await RequireCodeAsync(user, code, recoveryCode: true);
        UserViews.Check(await users.DisableTwoFactorAsync(user));
    }

    public static string FormatKey(string key) =>
        string.Join(' ', key.ToLowerInvariant().Chunk(4).Select(group => new string(group)));

    private async Task<IReadOnlyList<string>> NewRecoveryCodesAsync(CmsUser user) =>
        [.. await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount) ?? throw new InvalidOperationException("Identity generated no recovery codes.")];

    private async Task RequireOffAsync(CmsUser user)
    {
        if (await IsOnAsync(user))
        {
            throw new ValidationException([new ValidationError("twoFactor", IdentityErrorCodes.TwoFactorEnabled, "Two-factor sign-in is on; turn it off before you enrol another authenticator.")]);
        }
    }

    private async Task RequireOnAsync(CmsUser user)
    {
        if (!await IsOnAsync(user))
        {
            throw new ValidationException([new ValidationError("twoFactor", IdentityErrorCodes.TwoFactorNotEnabled, "Two-factor sign-in is off.")]);
        }
    }

    // A wrong code counts as a failed sign-in, so the lockout of cms.Users bounds guessing as it does on the hosted pages.
    private async Task RequireCodeAsync(CmsUser user, string? code, bool recoveryCode)
    {
        // Identity's recovery codes are upper case.
        var compact = (code ?? "").Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
        if (compact.Length > 0 && !await users.IsLockedOutAsync(user))
        {
            var authenticator = compact.Replace("-", "", StringComparison.Ordinal);
            if (await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, authenticator)
                || (recoveryCode && (await users.RedeemTwoFactorRecoveryCodeAsync(user, compact)).Succeeded))
            {
                UserViews.Check(await users.ResetAccessFailedCountAsync(user));
                return;
            }

            UserViews.Check(await users.AccessFailedAsync(user));
        }

        throw new ValidationException([new ValidationError("code", IdentityErrorCodes.InvalidTwoFactorCode, "The code is not right, or the account is locked out for now.")]);
    }

    private static AuthenticatorSetup Setup(CmsUser user, string key, string issuer)
    {
        var account = Uri.EscapeDataString(user.Email ?? user.UserName ?? user.Id.ToString("D"));
        var label = Uri.EscapeDataString(issuer);
        return new AuthenticatorSetup(key, $"otpauth://totp/{label}:{account}?secret={key}&issuer={label}&digits=6");
    }
}
