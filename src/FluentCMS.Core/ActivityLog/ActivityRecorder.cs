using FluentCMS.Activity;
using FluentCMS.Events;

namespace FluentCMS.Core.ActivityLog;

// What an event is in the activity log: one description per event type, registered with cms.Events.AddActivity.
internal sealed class ActivityDescription<TEvent>(Func<TEvent, ActivityEntry> describe)
    where TEvent : CmsEvent
{
    public ActivityEntry Describe(TEvent message) => describe(message);
}

// Subscribes through the outbox, so that a crash between a commit and its delivery loses no row. The delivery key is the id
// of the row, so that a repeated delivery writes nothing. The row says what the event says: who, where and when come from
// its source, not from the job that delivers it. A row still waiting when the log is turned off is delivered and dropped.
internal sealed class ActivityRecorder<TEvent>(ActivityDescription<TEvent> description, ActivityLogWriter writer, ActivityOptions options) : IOutboxEventHandler<TEvent>
    where TEvent : CmsEvent
{
    public ValueTask HandleAsync(TEvent message, EventDelivery delivery, CancellationToken cancellationToken) =>
        options.Enabled
            ? writer.WriteAsync(description.Describe(message), message.Source, delivery.Key, cancellationToken)
            : ValueTask.CompletedTask;
}
