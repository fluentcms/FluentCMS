using FluentCMS.Content;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Content;

internal enum EntryChangeKind
{
    DraftSaved,
    Trashed,
    Restored,
}

internal sealed record EntryChange(EntryChangeKind Kind, string Type, Guid Id, string Locale, int Version, OperationContext Operation)
{
    // The id of the event the change becomes, so that the outbox row and the handlers of this process agree on it.
    public Guid EventId { get; } = Guid.CreateVersion7();
}

// The seam for the event bus: the service tells it what changed once its transaction has committed, one change
// per locale, and tells it nothing when that rolls back. The write has happened by then, so a failure here is logged and
// goes no further (spec 4.11). The service cannot see the end of a transaction it did not begin, so it tells nothing of
// a write inside one: whoever needs the changes of several writes in one transaction, such as #2703, runs them in
// ContentService.InTransactionAsync.
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
