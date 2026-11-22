using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Caching;

// 9160 to 9169, the event ids of the distributed lock.
internal static partial class LockLog
{
    public const int LeaseTakenOverId = 9160;
    public const int LostId = 9161;
    public const int RenewFailedId = 9162;
    public const int ReleaseFailedId = 9163;
    public const int WentOnWithoutLockId = 9164;
    public const int RenewWaitingId = 9165;
    public const int TimedOutBusyId = 9166;
    public const int LostCallbackFailedId = 9167;
    public const int LostCallbackSlowId = 9168;

    [LoggerMessage(EventId = LeaseTakenOverId, EventName = "LockLeaseTakenOver", Level = LogLevel.Warning, Message = "Took the lock {Name} after the lease of {Holder} ran out; that holder stopped without releasing it or, on a database with one writer, could not renew it behind other writes.")]
    public static partial void LeaseTakenOver(ILogger logger, string name, string holder);

    [LoggerMessage(EventId = LostId, EventName = "LockLost", Level = LogLevel.Warning, Message = "Lost the lock {Name} while holding it, because {Reason}.")]
    public static partial void Lost(ILogger logger, string name, string reason);

    [LoggerMessage(EventId = RenewFailedId, EventName = "LockRenewFailed", Level = LogLevel.Warning, Message = "Could not renew the lease of the lock {Name}; it is tried again until the lease runs out.")]
    public static partial void RenewFailed(ILogger logger, Exception exception, string name);

    [LoggerMessage(EventId = ReleaseFailedId, EventName = "LockReleaseFailed", Level = LogLevel.Warning, Message = "Could not release the lock {Name}; another holder can take it when its lease runs out.")]
    public static partial void ReleaseFailed(ILogger logger, Exception exception, string name);

    [LoggerMessage(EventId = WentOnWithoutLockId, EventName = "LockWentOnWithout", Level = LogLevel.Warning, Message = "The lock {Name} was not free within {Timeout}, so {Work} went on without it.")]
    public static partial void WentOnWithoutLock(ILogger logger, string name, TimeSpan timeout, string work);

    [LoggerMessage(EventId = RenewWaitingId, EventName = "LockRenewWaiting", Level = LogLevel.Information, Message = "The lease of the lock {Name} is renewed once another connection's write transaction ends; the database has one writer, so no other holder can take the lock meanwhile.")]
    public static partial void RenewWaiting(ILogger logger, string name);

    [LoggerMessage(EventId = TimedOutBusyId, EventName = "LockTimedOutBusy", Level = LogLevel.Warning, Message = "The lock {Name} was not taken within {Timeout}, and the last try gave up waiting for another connection's write transaction; the database has one writer, so the lock may be free while that transaction lasts.")]
    public static partial void TimedOutBusy(ILogger logger, string name, TimeSpan timeout);

    [LoggerMessage(EventId = LostCallbackFailedId, EventName = "LockLostCallbackFailed", Level = LogLevel.Warning, Message = "A callback that the holder of the lock {Name} registered on its Lost token failed; the lock is lost all the same, and the other callbacks ran.")]
    public static partial void LostCallbackFailed(ILogger logger, Exception exception, string name);

    [LoggerMessage(EventId = LostCallbackSlowId, EventName = "LockLostCallbackSlow", Level = LogLevel.Warning, Message = "A callback that the holder of the lock {Name} registered on its Lost token had not returned within {Wait}, so the handle was disposed without waiting for it any longer; the callback runs on, and one that throws when it returns is still logged.")]
    public static partial void LostCallbackSlow(ILogger logger, string name, TimeSpan wait);
}
