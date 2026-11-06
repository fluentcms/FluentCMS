using FluentCMS.Content;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Core.Identity;

internal sealed record UserLogin(string Provider, string ProviderKey, string? DisplayName);

// A user's external logins, and their removal, for whoever the caller checked may act on the user. A login is a way to sign
// in while its provider is offered on the login page, and a password while local login is on; removing the last way is
// refused, since the user could then not sign in again. Removing a login gives a new security stamp, which ends the user's
// sessions, as a new password does.
internal sealed class UserLogins(CmsUserManager users, IdentityChanges changes, IAuthenticationSchemeProvider schemes, SsoOptions sso)
{
    public async Task<IReadOnlyList<UserLogin>> ListAsync(CmsUser user) =>
        [.. (await users.GetLoginsAsync(user))
            .OrderBy(login => login.LoginProvider, StringComparer.Ordinal).ThenBy(login => login.ProviderKey, StringComparer.Ordinal)
            .Select(login => new UserLogin(login.LoginProvider, login.ProviderKey, login.ProviderDisplayName))];

    // Every login of the provider, which is normally one.
    public async Task RemoveAsync(CmsUser user, string provider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(provider);

        var logins = await users.GetLoginsAsync(user);
        List<UserLoginInfo> removed = [.. logins.Where(login => string.Equals(login.LoginProvider, provider, StringComparison.OrdinalIgnoreCase))];
        if (removed.Count == 0)
        {
            throw new ValidationException([new ValidationError("provider", IdentityErrorCodes.LoginNotFound, $"The user has no external login of '{provider}'.")]);
        }

        var offered = (await schemes.GetAllSchemesAsync())
            .Where(scheme => !string.IsNullOrEmpty(scheme.DisplayName))
            .Select(scheme => scheme.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var left = logins.Count(login => !removed.Contains(login) && offered.Contains(login.LoginProvider))
            + (!sso.DisableLocalLogin && await users.HasPasswordAsync(user) ? 1 : 0);
        if (left == 0 && removed.Any(login => offered.Contains(login.LoginProvider)))
        {
            throw new ValidationException([new ValidationError("provider", IdentityErrorCodes.LastWayIn, "This login is the user's last way to sign in. Link another provider or, with local login on, set a password first.")]);
        }

        UserViews.Check(await changes.RunAsync(
            async () =>
            {
                foreach (var login in removed)
                {
                    if (await users.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey) is { Succeeded: false } failed)
                    {
                        return failed;
                    }
                }

                return IdentityResult.Success;
            },
            cancellationToken));
    }
}
