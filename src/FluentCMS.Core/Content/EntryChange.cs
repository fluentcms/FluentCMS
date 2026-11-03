using FluentCMS.Content;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Content;

internal enum EntryChangeKind
{
    DraftSaved,
    Trashed,
    Restored,
    Published,
    Unpublished,
    DraftDiscarded,
    VersionRestored,
    Purged,
}

internal sealed record EntryChange(EntryChangeKind Kind, string Type, Guid Id, string Locale, int Version, OperationContext Operation)
{
    // The id of the event the change becomes, so that the outbox row and the handlers of this process agree on it.
    public Guid EventId { get; } = Guid.CreateVersion7();
}

// The seam for the event bus: the service tells it what changed once its transaction has committed, one change
// per locale, and tells it nothing when that rolls back. The write has happened by then, so a failure here is logged and
// goes no further (spec 4.11). The service cannot see the end of a transaction it did not begin, so it tells this sink
// nothing of a write inside one, and the handlers of this process hear nothing of it; whoever needs the changes of several
// writes in one transaction, such as #2703, runs them in ContentService.InTransactionAsync. The rows for the outbox handlers
// are written inside that transaction through IEntryChangeStager, so they commit or roll back with it and are delivered.
internal interface IEntryChangeSink
{
    ValueTask PublishAsync(EntryChange change, CancellationToken cancellationToken);
}

// Hears the changes of a transaction inside it, just before the commit, so that what must survive a crash can be written
// in the same transaction. A failure here fails the write.
internal interface IEntryChangeStager
{
    ValueTask StageAsync(DbContext context, IReadOnlyList<EntryChange> changes, CancellationToken cancellationToken);
}
