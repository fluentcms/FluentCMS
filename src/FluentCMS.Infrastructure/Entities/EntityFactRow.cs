namespace FluentCMS.Infrastructure.Entities;

// One fact of one entity version, written with the version and never changed (ENT-02). Times are UTC, so both providers compare
// them in queries, as the stale facts query does (ENT-03).
public sealed class EntityFactRow
{
    public Guid Id { get; set; }

    public Guid EntryId { get; set; }

    public int Version { get; set; }

    // The field's place in the version's document, which follows the definition.
    public int Position { get; set; }

    public string Field { get; set; } = string.Empty;

    public string? SourceKind { get; set; }

    public string? SourceReference { get; set; }

    public Guid? Owner { get; set; }

    public DateTime? ValidFrom { get; set; }

    public DateTime? ValidUntil { get; set; }

    public DateTime? LastVerified { get; set; }

    public bool Unsourced { get; set; }
}
