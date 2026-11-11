using FluentCMS.Content;
using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Core.Identity.Members;

// An external login of a member as the APIs show it. The key the provider knows the member by is not shown.
internal sealed record MemberLogin(string Provider, string? DisplayName);

// A member's external logins, and their removal, for whoever the caller checked may act on the member. A login is a way
// to sign in while its provider is one of cms.Members.Providers, and a password is one while the member has it; removing
// the last way is refused, since the member could then not sign in again. Removing a login gives a new security stamp
// and ends the member's sessions, access tokens included, as a new password does.
internal sealed class MemberLogins(MemberManager members, MemberOptions options, MemberSessions sessions)
{
    public async Task<IReadOnlyList<MemberLogin>> ListAsync(CmsMember member) =>
        [.. (await members.GetLoginsAsync(member))
            .OrderBy(login => login.LoginProvider, StringComparer.Ordinal)
            .Select(login => new MemberLogin(login.LoginProvider, login.ProviderDisplayName))];

    // Every login of the provider, which is one.
    public async Task RemoveAsync(CmsMember member, string provider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(provider);

        var logins = await members.GetLoginsAsync(member);
        List<UserLoginInfo> removed = [.. logins.Where(login => string.Equals(login.LoginProvider, provider, StringComparison.OrdinalIgnoreCase))];
        if (removed.Count == 0)
        {
            throw new ValidationException([new ValidationError("provider", MemberErrorCodes.LoginNotFound, $"The member has no external login of '{provider}'.")]);
        }

        var left = logins.Count(login => !removed.Contains(login) && options.Providers.ContainsKey(login.LoginProvider))
            + (member.PasswordHash is not null ? 1 : 0);
        if (left == 0 && removed.Any(login => options.Providers.ContainsKey(login.LoginProvider)))
        {
            throw new ValidationException([new ValidationError("provider", MemberErrorCodes.LastWayIn, "This login is the member's last way to sign in. Link another provider, or choose a password with a reset link, first.")]);
        }

        MemberViews.Check(await members.RemoveLoginsAsync(member, removed));
        await sessions.RevokeAllAsync(member.Id, cancellationToken);
    }
}
