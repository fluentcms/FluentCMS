using FluentCMS.Content;
using FluentCMS.Events;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Core.Identity;

// The sign-in events of the hosted pages, which run outside any operation: a success is the user's, a failure is the
// anonymous principal's, both from the hosted pages (Origin.Admin, the admin users' way in) and of no site. They are
// written even when the client goes away, so that a dropped connection does not hide a failure.
internal sealed class SignInEvents(IdentityChanges changes, TimeProvider time)
{
    public Task SucceededAsync(HttpContext context, CmsUser user, string method) =>
        changes.RecordAsync(
            new LoginSucceededEvent(user.Id, user.UserName ?? "", method, Address(context))
            {
                Source = Source(context, user.Id, user.UserName ?? "", PrincipalKind.AdminUser),
            },
            CancellationToken.None);

    public Task FailedAsync(HttpContext context, CmsUser? user, string method, string reason) =>
        changes.RecordAsync(
            new LoginFailedEvent(user?.Id, user?.UserName, method, reason, Address(context))
            {
                Source = Source(context, Principal.Anonymous.Id, Principal.Anonymous.Name, PrincipalKind.Anonymous),
            },
            CancellationToken.None);

    private EventSource Source(HttpContext context, Guid actorId, string actorName, PrincipalKind kind) =>
        new(time.GetUtcNow(), Guid.Empty, actorId, actorName, kind, Origin.Admin, System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier);

    private static string? Address(HttpContext context) =>
        context.Connection.RemoteIpAddress is { } address
            ? (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString()
            : null;
}
