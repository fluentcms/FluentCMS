using FluentCMS.Core.Content;
using FluentCMS.Events;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Events;

// Turns what the content service tells it into events. Inside the transaction it records the rows of the outbox
// handlers, so that they commit with the write; after the commit it runs the handlers of this process.
internal sealed class EntryChangeSink(EventDispatcher dispatcher, OutboxWriter outbox) : IEntryChangeSink, IEntryChangeStager
{
    public ValueTask StageAsync(DbContext context, IReadOnlyList<EntryChange> changes, CancellationToken cancellationToken) =>
        outbox.StageAsync(context, changes.Select(EntryEvents.From), cancellationToken);

    public ValueTask PublishAsync(EntryChange change, CancellationToken cancellationToken) =>
        dispatcher.DispatchAsync(EntryEvents.From(change));
}

internal static class EntryEvents
{
    public static EntryEvent From(EntryChange change)
    {
        var source = EventSource.From(change.Operation);
        return change.Kind switch
        {
            EntryChangeKind.DraftSaved => new EntryDraftSavedEvent(change.Type, change.Id, change.Locale, change.Version) { EventId = change.EventId, Source = source },
            EntryChangeKind.Trashed => new EntryTrashedEvent(change.Type, change.Id, change.Locale, change.Version) { EventId = change.EventId, Source = source },
            EntryChangeKind.Restored => new EntryRestoredEvent(change.Type, change.Id, change.Locale, change.Version) { EventId = change.EventId, Source = source },
            EntryChangeKind.Published => new EntryPublishedEvent(change.Type, change.Id, change.Locale, change.Version) { EventId = change.EventId, Source = source },
            EntryChangeKind.Unpublished => new EntryUnpublishedEvent(change.Type, change.Id, change.Locale, change.Version) { EventId = change.EventId, Source = source },
            EntryChangeKind.DraftDiscarded => new EntryDraftDiscardedEvent(change.Type, change.Id, change.Locale, change.Version) { EventId = change.EventId, Source = source },
            EntryChangeKind.VersionRestored => new EntryVersionRestoredEvent(change.Type, change.Id, change.Locale, change.Version) { EventId = change.EventId, Source = source },
            _ => throw new ArgumentOutOfRangeException(nameof(change), change.Kind, "No event is defined for this kind of change."),
        };
    }
}
