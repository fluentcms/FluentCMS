using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Events;

internal static partial class EventLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "The handler {Handler} failed for {EventType} {EventId}; the write stands.")]
    public static partial void HandlerFailed(ILogger logger, Exception exception, string handler, string eventType, Guid eventId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox delivery {Id} to {Handler} failed on attempt {Attempt}: {Error}")]
    public static partial void DeliveryFailed(ILogger logger, Guid id, string handler, int attempt, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox delivery {Id} to {Handler} is out of attempts after {Attempt}: {Error}")]
    public static partial void DeliveryGaveUp(ILogger logger, Guid id, string handler, int attempt, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not write the outcome of outbox delivery {Id}; it is delivered again when its lease ends.")]
    public static partial void SettleFailed(ILogger logger, Exception exception, Guid id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not ask for the outbox to be drained; the next scheduled drain delivers what was recorded.")]
    public static partial void KickFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not register the recurring drain of the outbox; events are delivered only after a write asks for it.")]
    public static partial void RecurringFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "The handlers of {EventType} {EventId} could not be built, so none of them ran; the write stands.")]
    public static partial void HandlersUnavailable(ILogger logger, Exception exception, string eventType, Guid eventId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox delivery {Id} waits: no handler is registered as '{Handler}' on this instance.")]
    public static partial void HandlerMissing(ILogger logger, Guid id, string handler);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Count} outbox deliveries ran out of attempts without a result, since the workers that held them stopped; they are left as failed.")]
    public static partial void Exhausted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox delivery {Id} to {Handler} lost its lease, so this worker leaves it alone.")]
    public static partial void LeaseLost(ILogger logger, Guid id, string handler);

    [LoggerMessage(Level = LogLevel.Error, Message = "The drain of the outbox hit an error.")]
    public static partial void DrainFailed(ILogger logger, Exception exception);
}
