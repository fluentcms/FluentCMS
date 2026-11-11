using System.Net;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Identity.Members;

// 9070 to 9089, the event ids of the member pool, whose codes MemberErrorCodes has. A line names a member by its id,
// never by its address, and holds no token and no password.
internal static partial class MemberLog
{
    public const int MailFailedId = 9070;
    public const int NoMailLinkId = 9071;
    public const int MailHeldBackId = 9072;
    public const int UnverifiedMembersRemovedId = 9073;
    public const int CleanupScheduleFailedId = 9074;
    public const int DirectLoginRefusedId = 9075;

    [LoggerMessage(EventId = MailFailedId, EventName = "MemberMailFailed", Level = LogLevel.Error, Message = "Could not send a mail to member {MemberId}; the caller was answered as if it had gone.")]
    public static partial void MailFailed(ILogger logger, Guid memberId, Exception exception);

    [LoggerMessage(EventId = NoMailLinkId, EventName = "MemberMailLinkUnavailable", Level = LogLevel.Error, Message = "FCMS3173: no mail was sent to member {MemberId}, because its link has nowhere to go. Set FluentCms:PublicOrigin, or the FluentCms:Members:Links templates of the front end; a link is never built from a request.")]
    public static partial void NoMailLink(ILogger logger, Guid memberId);

    [LoggerMessage(EventId = MailHeldBackId, EventName = "MemberMailHeldBack", Level = LogLevel.Information, Message = "Held back a mail to member {MemberId}: its address got one less than a minute ago.")]
    public static partial void MailHeldBack(ILogger logger, Guid memberId);

    [LoggerMessage(EventId = UnverifiedMembersRemovedId, EventName = "UnverifiedMembersRemoved", Level = LogLevel.Information, Message = "Removed {Count} member(s) that had not verified their address since {Threshold}.")]
    public static partial void UnverifiedMembersRemoved(ILogger logger, int count, DateTimeOffset threshold);

    [LoggerMessage(EventId = CleanupScheduleFailedId, EventName = "MemberCleanupScheduleFailed", Level = LogLevel.Error, Message = "Could not register the recurring job that removes members who never verified their address; they stay until it is registered.")]
    public static partial void CleanupScheduleFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = DirectLoginRefusedId, EventName = "MemberDirectLoginRefused", Level = LogLevel.Information, Message = "Refused a direct login of client {ClientId} from {Address}: {Reason}. The client was answered invalid_grant.")]
    public static partial void DirectLoginRefused(ILogger logger, string? clientId, IPAddress? address, string reason);
}
