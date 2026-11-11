using FluentCMS.Core.Identity.Oidc;
using OpenIddict.Abstractions;

namespace FluentCMS.Core.Identity.Members;

// The sessions of a member: the authorizations of its sign-ins, as UserSessions lists and revokes them for an admin
// user, since a member's id is the subject of its tokens. Ending them all also revokes every token of the member, so
// that an access token of a direct login, which has no authorization, stops at once too.
internal sealed class MemberSessions(UserSessions sessions, IOpenIddictAuthorizationManager authorizations, IOpenIddictTokenManager tokens)
{
    public Task<IReadOnlyList<UserSession>> ListAsync(Guid memberId, CancellationToken cancellationToken) =>
        sessions.ListAsync(memberId, cancellationToken);

    public Task<bool> RevokeAsync(Guid memberId, Guid sessionId, CancellationToken cancellationToken) =>
        sessions.RevokeAsync(memberId, sessionId, cancellationToken);

    public async Task RevokeAllAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var subject = memberId.ToString("D");
        await authorizations.RevokeBySubjectAsync(subject, cancellationToken);
        await tokens.RevokeBySubjectAsync(subject, cancellationToken);
    }
}
