using System.Text.Json;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Content;

// A person's create or update of an entry from a generated form (FRM-01, FRM-02). The version and its operation go into a change set
// that the save opens, in one unit of work, so a person's edit is reviewed like an agent's: the set stays open until it is submitted.
public interface IEntryEdits
{
    Task<Result<SavedEntryEdit>> SaveAsync(EntryEdit edit, CancellationToken cancellationToken = default);
}

// EntryId and BaseVersion name the entry and the version the form read for an update, and are null for a create.
public sealed record EntryEdit(string ContentType, Guid? EntryId, int? BaseVersion, JsonElement Fields, NewChangeSet ChangeSet)
{
    // The published version an update's values came from, when they came from it rather than from the latest draft. If publishing
    // has moved on, the save is refused, so it cannot undo what was published meanwhile.
    public int? PublishedBase { get; init; }
}

public sealed record SavedEntryEdit(Entry Entry, ChangeSet ChangeSet);
