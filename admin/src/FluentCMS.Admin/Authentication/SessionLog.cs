using Microsoft.Extensions.Logging;

namespace FluentCMS.Admin.Authentication;

// What the browser's console shows of the sign-in. No message holds a token, a code or a secret of a sign-in.
internal static partial class SessionLog
{
    [LoggerMessage(EventId = 10, EventName = "SignInFailed", Level = LogLevel.Warning, Message = "The sign-in did not complete: {Reason} {Error}.")]
    public static partial void SignInFailed(ILogger logger, SignInFailure reason, string? error);

    [LoggerMessage(EventId = 11, EventName = "SessionEnded", Level = LogLevel.Information, Message = "The session ended: the token endpoint refused the refresh with {Error}.")]
    public static partial void SessionEnded(ILogger logger, string error);

    [LoggerMessage(EventId = 12, EventName = "RefreshUnavailable", Level = LogLevel.Warning, Message = "The access token could not be refreshed; the session is kept.")]
    public static partial void RefreshUnavailable(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 13, EventName = "RevokeFailed", Level = LogLevel.Warning, Message = "The refresh token could not be revoked at sign-out; the tab forgets it all the same.")]
    public static partial void RevokeFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 14, EventName = "DraftsNotKept", Level = LogLevel.Error, Message = "The unsaved forms could not be kept when the session ended.")]
    public static partial void DraftsNotKept(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 16, EventName = "SessionEndedAfterSignIn", Level = LogLevel.Warning, Message = "The server ended the session right after the sign-in; the admin does not sign in again on its own.")]
    public static partial void SessionEndedAfterSignIn(ILogger logger);

    [LoggerMessage(EventId = 15, EventName = "SignInUnavailable", Level = LogLevel.Error, Message = "The sign-in could not start.")]
    public static partial void SignInUnavailable(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 17, EventName = "TabStorageRefused", Level = LogLevel.Error, Message = "The sign-in could not start: the browser refused the tab's sessionStorage.")]
    public static partial void TabStorageRefused(ILogger logger, Exception exception);
}
