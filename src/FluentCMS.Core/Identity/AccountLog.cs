using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Identity;

// 90xx, the event ids of the identity family (FCMS30xx); the hosted pages have 9004 to 9019.
internal static partial class AccountLog
{
    public const int ResetEmailFailedId = 9004;

    [LoggerMessage(EventId = ResetEmailFailedId, EventName = "ResetEmailFailed", Level = LogLevel.Error, Message = "Could not send the password reset email of user {UserId}; the page answered as it does for an unknown address.")]
    public static partial void ResetEmailFailed(ILogger logger, Guid userId, Exception exception);
}
