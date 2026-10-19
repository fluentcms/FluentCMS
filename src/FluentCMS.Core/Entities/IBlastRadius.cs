using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Entities;

// What a change set's entity changes do to the live site (ENT-04, CHG-03). Pages store references to facts, not their values, so
// they need no new version; publishing the entities changes what they resolve to, and the blast radius shows that change before it
// happens. A read from the reference index and the versions the set names (ADR-0005), with no audit event.
public interface IBlastRadius
{
    Task<Result<BlastRadius>> ForChangeSetAsync(Guid changeSetId, CancellationToken cancellationToken = default);
}

public sealed record BlastRadius(Guid ChangeSetId, IReadOnlyList<AffectedEntry> Entries);

// A published entry that shows a fact the set changes, with one diff per path, such as /price or /prices/1: the value the entity's
// base version gives and the value its proposed version gives. A create has no value before and a delete none after.
public sealed record AffectedEntry(Guid EntryId, string ContentType, int Version, IReadOnlyList<ValueDiff> Fields);
