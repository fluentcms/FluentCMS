using FluentCMS.Content;

namespace FluentCMS.Core.Content;

internal enum EntryChangeKind
{
    DraftSaved,
    Trashed,
    Restored,
}

internal sealed record EntryChange(EntryChangeKind Kind, string Type, Guid Id, string Locale, int Version, OperationContext Operation);

// The seam for the event bus of #2697: the service tells it what changed once the outermost transaction has committed,
// and tells it nothing when that rolls back. The write has happened by then, so an implementation must not throw.
internal interface IEntryChangeSink
{
    ValueTask PublishAsync(EntryChange change, CancellationToken cancellationToken);
}
