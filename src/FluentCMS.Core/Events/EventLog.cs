using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Events;

internal static partial class EventLog
{
    [LoggerMessage(EventId = 9611, Level = LogLevel.Error, Message = "The handler {Handler} failed for {EventType} {EventId}; the write stands.")]
    public static partial void HandlerFailed(ILogger logger, Exception exception, string handler, string eventType, Guid eventId);

    [LoggerMessage(EventId = 9601, Level = LogLevel.Warning, Message = "Outbox delivery {Id} to {Handler} failed on attempt {Attempt}: {Error}")]
    public static partial void DeliveryFailed(ILogger logger, Guid id, string handler, int attempt, string error);

    [LoggerMessage(EventId = 9602, Level = LogLevel.Error, Message = "Outbox delivery {Id} to {Handler} is out of attempts after {Attempt}: {Error}")]
    public static partial void DeliveryGaveUp(ILogger logger, Guid id, string handler, int attempt, string error);

    [LoggerMessage(EventId = 9603, Level = LogLevel.Warning, Message = "Could not write the outcome of outbox delivery {Id}; it is delivered again when its lease ends.")]
    public static partial void SettleFailed(ILogger logger, Exception exception, Guid id);

    [LoggerMessage(EventId = 9604, Level = LogLevel.Warning, Message = "Could not ask for the outbox to be drained; the next scheduled drain delivers what was recorded.")]
    public static partial void KickFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9605, Level = LogLevel.Error, Message = "Could not register the recurring drain of the outbox; events are delivered only after a write asks for it.")]
    public static partial void RecurringFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9612, Level = LogLevel.Error, Message = "The handlers of {EventType} {EventId} could not be built, so none of them ran; the write stands.")]
    public static partial void HandlersUnavailable(ILogger logger, Exception exception, string eventType, Guid eventId);

    [LoggerMessage(EventId = 9606, Level = LogLevel.Warning, Message = "Outbox delivery {Id} waits: no handler is registered as '{Handler}' on this instance.")]
    public static partial void HandlerMissing(ILogger logger, Guid id, string handler);

    [LoggerMessage(EventId = 9607, Level = LogLevel.Error, Message = "{Count} outbox deliveries ran out of attempts without a result, since the workers that held them stopped; they are left as failed.")]
    public static partial void Exhausted(ILogger logger, int count);

    [LoggerMessage(EventId = 9608, Level = LogLevel.Warning, Message = "Outbox delivery {Id} to {Handler} lost its lease, so this worker leaves it alone.")]
    public static partial void LeaseLost(ILogger logger, Guid id, string handler);

    [LoggerMessage(EventId = 9609, Level = LogLevel.Error, Message = "Outbox delivery {Id} to {Handler} did not finish within {Timeout} and was abandoned; the handler may still be running.")]
    public static partial void DeliveryAbandoned(ILogger logger, Guid id, string handler, TimeSpan timeout);

    [LoggerMessage(EventId = 9613, Level = LogLevel.Error, Message = "The in-process handlers of {EventType} {EventId} did not finish within {Timeout} and were abandoned; they may still be running.")]
    public static partial void DispatchAbandoned(ILogger logger, string eventType, Guid eventId, TimeSpan timeout);

    [LoggerMessage(EventId = 9614, Level = LogLevel.Warning, Message = "The handler of outbox delivery {Id} to {Handler} did not start within {Timeout}, so the delivery was put back without using an attempt; a later drain delivers it.")]
    public static partial void HandOffAbandoned(ILogger logger, Guid id, string handler, TimeSpan timeout);

    [LoggerMessage(EventId = 9615, Level = LogLevel.Warning, Message = "The handler {Handler} of {EventType} {EventId} did not finish within {Timeout}; its token was cancelled and the caller went on without it.")]
    public static partial void HandlerAbandoned(ILogger logger, string handler, string eventType, Guid eventId, TimeSpan timeout);

    [LoggerMessage(EventId = 9616, Level = LogLevel.Warning, Message = "The handler {Handler} of {EventType} {EventId} took {Duration}, which the caller waited for.")]
    public static partial void HandlerSlow(ILogger logger, string handler, string eventType, Guid eventId, TimeSpan duration);

    [LoggerMessage(EventId = 9617, Level = LogLevel.Warning, Message = "The handler {Handler} of {EventType} {EventId} failed after it was abandoned; the write stands.")]
    public static partial void AbandonedHandlerFailed(ILogger logger, Exception exception, string handler, string eventType, Guid eventId);

    [LoggerMessage(EventId = 9618, Level = LogLevel.Warning, Message = "The scope of the handlers of {EventType} {EventId} failed while it was disposed; the write stands.")]
    public static partial void HandlerScopeFailed(ILogger logger, Exception exception, string eventType, Guid eventId);

    [LoggerMessage(EventId = 9610, Level = LogLevel.Error, Message = "The drain of the outbox hit an error.")]
    public static partial void DrainFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9619, Level = LogLevel.Error, Message = "Outbox delivery {Id} to {Handler} waited longer than {OrphanAfter} for its handler or the handler's plugin, so it is left as failed.")]
    public static partial void Orphaned(ILogger logger, Guid id, string handler, TimeSpan orphanAfter);

    [LoggerMessage(EventId = 9620, Level = LogLevel.Warning, Message = "The handler of outbox delivery {Id} to {Handler} failed after it was abandoned; the delivery was settled already.")]
    public static partial void AbandonedDeliveryFailed(ILogger logger, Exception exception, Guid id, string handler);

    [LoggerMessage(EventId = 9621, Level = LogLevel.Warning, Message = "The scope of outbox delivery {Id} to {Handler} failed while it was disposed.")]
    public static partial void DeliveryScopeFailed(ILogger logger, Exception exception, Guid id, string handler);

    [LoggerMessage(EventId = 9622, Level = LogLevel.Information, Message = "The prune of the outbox removed {Delivered} delivered and {Failed} failed rows.")]
    public static partial void Pruned(ILogger logger, int delivered, int failed);
}
