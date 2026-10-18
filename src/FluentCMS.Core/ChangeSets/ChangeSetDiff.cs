namespace FluentCMS.Core.ChangeSets;

// The field-level diff of every item in a change set, in the order of its operations (CHG-03).
public sealed record ChangeSetDiff(Guid ChangeSetId, IReadOnlyList<ItemDiff> Items);
