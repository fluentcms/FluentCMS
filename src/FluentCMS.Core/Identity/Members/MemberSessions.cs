using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Events;
using OpenIddict.Abstractions;

namespace FluentCMS.Core.Identity.Members;

// The sessions of a member: the authorizations of its sign-ins, as UserSessions lists and revokes them for an admin
// user, since a member's id is the subject of its tokens. Ending them all also revokes every token of the member, so
// that an access token of a direct login, which has no authorization, stops at once too. What ends writes an activity row
// with the reason (#3207).
internal sealed class MemberSessions(UserSessions sessions, SessionEvents events, IOpenIddictAuthorizationManager authorizations, IOpenIddictTokenManager tokens, TimeProvider time)
{
    public Task<IReadOnlyList<UserSession>> ListAsync(Guid memberId, CancellationToken cancellationToken) =>
        sessions.ListAsync(memberId, cancellationToken);

    public Task<bool> RevokeAsync(CmsMember member, Guid sessionId, CancellationToken cancellationToken) =>
        events.EndAsync(
            () => sessions.RevokeAsync(member.Id, sessionId, cancellationToken),
            new MemberSessionsEndedEvent(member.Id, member.Email ?? "", SessionEndReasons.Revoked, sessionId),
            cancellationToken);

    // Everything of the member is revoked whatever it held; the row is written only when a session was live, since the
    // counts of the bulk revocations take in what was revoked, redeemed or expired already (#3531).
    public Task RevokeAllAsync(CmsMember member, string reason, CancellationToken cancellationToken) =>
        events.EndAsync(
            async () =>
            {
                var subject = member.Id.ToString("D");
                var live = await HasLiveSessionAsync(subject, cancellationToken);
                await authorizations.RevokeBySubjectAsync(subject, cancellationToken);
                await tokens.RevokeBySubjectAsync(subject, cancellationToken);
                return live;
            },
            new MemberSessionsEndedEvent(member.Id, member.Email ?? "", reason, null),
            cancellationToken);

    // A valid authorization, as UserSessions counts them, or a valid token that has not expired, as a direct login's are.
    private async Task<bool> HasLiveSessionAsync(string subject, CancellationToken cancellationToken)
    {
        await foreach (var authorization in authorizations.FindBySubjectAsync(subject, cancellationToken))
        {
            if (await authorizations.HasStatusAsync(authorization, OpenIddictConstants.Statuses.Valid, cancellationToken))
            {
                return true;
            }
        }

        var now = time.GetUtcNow();
        await foreach (var token in tokens.FindBySubjectAsync(subject, cancellationToken))
        {
            if (!await tokens.HasStatusAsync(token, OpenIddictConstants.Statuses.Valid, cancellationToken))
            {
                continue;
            }

            var expires = await tokens.GetExpirationDateAsync(token, cancellationToken);
            if (expires is null || expires > now)
            {
                return true;
            }
        }

        return false;
    }
}
