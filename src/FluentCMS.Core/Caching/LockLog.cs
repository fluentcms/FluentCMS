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

    [LoggerMessage(EventId = LeaseTakenOverId, EventName = "LockLeaseTakenOver", Level = LogLevel.Warning, Message = "Took the lock {Name} after the lease of {Holder} ran out; that holder stopped without releasing it.")]
    public static partial void LeaseTakenOver(ILogger logger, string name, string holder);

    [LoggerMessage(EventId = LostId, EventName = "LockLost", Level = LogLevel.Warning, Message = "Lost the lock {Name} while holding it, because {Reason}.")]
    public static partial void Lost(ILogger logger, string name, string reason);

    [LoggerMessage(EventId = RenewFailedId, EventName = "LockRenewFailed", Level = LogLevel.Warning, Message = "Could not renew the lease of the lock {Name}; it is tried again until the lease runs out.")]
    public static partial void RenewFailed(ILogger logger, Exception exception, string name);

    [LoggerMessage(EventId = ReleaseFailedId, EventName = "LockReleaseFailed", Level = LogLevel.Warning, Message = "Could not release the lock {Name}; another holder can take it when its lease runs out.")]
    public static partial void ReleaseFailed(ILogger logger, Exception exception, string name);

    [LoggerMessage(EventId = WentOnWithoutLockId, EventName = "LockWentOnWithout", Level = LogLevel.Warning, Message = "The lock {Name} was not free within {Timeout}, so {Work} went on without it.")]
    public static partial void WentOnWithoutLock(ILogger logger, string name, TimeSpan timeout, string work);
}
