using System.Data.Common;
using System.Security.Claims;
using FluentCMS.Content;
using FluentCMS.Events;
using FluentCMS.Core.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Identity;

// The ends of sessions in both pools, for the activity log (#3207): a row per session ended by its id, and one per sign-in
// or user whose sessions ended together, with the reason. A row never carries a token or the id of a server session.
internal sealed class SessionEvents(IdentityChanges changes, TimeProvider time, ILogger<SessionEvents> logger)
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
    // the system when something else ended it. Of no site, and written even when the client goes away. The sign-in has
    // ended by then, its tokens revoked and its cookie deleted, so a row that cannot be written is logged and the request
    // goes on: a failure here would answer 500 and clear the response that deletes the cookie (#3531).
    public Task SignInEndedAsync(HttpContext context, bool members, string? userId, ClaimsPrincipal signedIn, string reason, bool bySystem = false) =>
        Guid.TryParse(userId, out var id) ? SignInEndedAsync(context, members, id, signedIn.Identity?.Name ?? "", reason, bySystem) : Task.CompletedTask;

    public async Task SignInEndedAsync(HttpContext context, bool members, Guid id, string name, string reason, bool bySystem = false)
    {
        CmsEvent ended = members ? new MemberSessionsEndedEvent(id, name, reason, null) : new UserSessionsEndedEvent(id, name, reason, null);
        var actor = bySystem ? Principal.System : new Principal(members ? PrincipalKind.Member : PrincipalKind.AdminUser, id, name);
        var source = new EventSource(time.GetUtcNow(), Guid.Empty, actor.Id, actor.Name, actor.Kind, members ? Origin.Api : Origin.Admin, System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier);
        try
        {
            await changes.RecordAsync(ended with { Source = source }, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException && !IsFaultOfTheCode(exception))
        {
            // The type and the database's codes only: with EF's sensitive data logging, the message can carry the row, a
            // name or an address.
            var database = Database(exception);
            AccountLog.SessionEndNotRecorded(logger, members ? "member" : "user", id, reason, (exception.InnerException ?? exception).GetType().FullName, database?.SqlState, database?.ErrorCode);
        }
    }

    // A write inside a transaction of someone else's is refused before the database is asked, and fails the request as it
    // did before; every other failure is the write's, whatever the provider throws for it (#3531).
    private static bool IsFaultOfTheCode(Exception exception) =>
        exception is InvalidOperationException { Message: var message }
        && (message.StartsWith(IdentityErrorCodes.WriteInTransaction, StringComparison.Ordinal)
            || message.StartsWith(EventErrorCodes.PublishInForeignTransaction, StringComparison.Ordinal));

    private static DbException? Database(Exception? exception)
    {
        for (; exception is not null; exception = exception.InnerException)
        {
            if (exception is DbException database)
            {
                return database;
            }
        }

        return null;
    }
}
