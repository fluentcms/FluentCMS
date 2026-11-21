using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Events;
using OpenIddict.Abstractions;

namespace FluentCMS.Core.Identity.Members;

// The sessions of a member: the authorizations of its sign-ins, as UserSessions lists and revokes them for an admin
// user, since a member's id is the subject of its tokens. Ending them all also revokes every token of the member, so
// that an access token of a direct login, which has no authorization, stops at once too. What ends writes an activity row
// with the reason (#3207).
internal sealed class MemberSessions(UserSessions sessions, SessionEvents events, IOpenIddictAuthorizationManager authorizations, IOpenIddictTokenManager tokens)
{
    public Task<IReadOnlyList<UserSession>> ListAsync(Guid memberId, CancellationToken cancellationToken) =>
        sessions.ListAsync(memberId, cancellationToken);

    public Task<bool> RevokeAsync(CmsMember member, Guid sessionId, CancellationToken cancellationToken) =>
        events.EndAsync(
            () => sessions.RevokeAsync(member.Id, sessionId, cancellationToken),
            new MemberSessionsEndedEvent(member.Id, member.Email ?? "", SessionEndReasons.Revoked, sessionId),
            cancellationToken);

    public Task RevokeAllAsync(CmsMember member, string reason, CancellationToken cancellationToken) =>
        events.EndAsync(
            async () =>
            {
                var subject = member.Id.ToString("D");
                var ended = await authorizations.RevokeBySubjectAsync(subject, cancellationToken);
                return ended + await tokens.RevokeBySubjectAsync(subject, cancellationToken) > 0;
            },
            new MemberSessionsEndedEvent(member.Id, member.Email ?? "", reason, null),
            cancellationToken);
}
