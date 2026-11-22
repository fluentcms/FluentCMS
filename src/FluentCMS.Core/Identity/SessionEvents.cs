using System.Security.Claims;
using FluentCMS.Content;
using FluentCMS.Events;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Core.Identity;

// The ends of sessions in both pools, for the activity log (#3207): a row per session ended by its id, and one per sign-in
// or user whose sessions ended together, with the reason. A row never carries a token or the id of a server session.
internal sealed class SessionEvents(IdentityChanges changes, TimeProvider time)
{
    // In an operation, whose caller is the actor, or as the system outside one: the row commits with the end, and an end
    // that ended nothing writes none.
    public Task<bool> EndAsync(Func<Task<bool>> end, CmsEvent ended, CancellationToken cancellationToken) =>
        changes.RunAsync(
            async () =>
            {
                if (!await end())
                {
                    return false;
                }

                await changes.RaiseAsync(ended, cancellationToken);
                return true;
            },
            cancellationToken);

    // A sign-in on the hosted pages ended, outside any operation, as the sign-ins are recorded: by its user or member, or by
    // the system when something else ended it. Of no site, and written even when the client goes away.
    public Task SignInEndedAsync(HttpContext context, bool members, string? userId, ClaimsPrincipal signedIn, string reason, bool bySystem = false) =>
        Guid.TryParse(userId, out var id) ? SignInEndedAsync(context, members, id, signedIn.Identity?.Name ?? "", reason, bySystem) : Task.CompletedTask;

    public Task SignInEndedAsync(HttpContext context, bool members, Guid id, string name, string reason, bool bySystem = false)
    {
        CmsEvent ended = members ? new MemberSessionsEndedEvent(id, name, reason, null) : new UserSessionsEndedEvent(id, name, reason, null);
        var actor = bySystem ? Principal.System : new Principal(members ? PrincipalKind.Member : PrincipalKind.AdminUser, id, name);
        var source = new EventSource(time.GetUtcNow(), Guid.Empty, actor.Id, actor.Name, actor.Kind, members ? Origin.Api : Origin.Admin, System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier);
        return changes.RecordAsync(ended with { Source = source }, CancellationToken.None);
    }
}
