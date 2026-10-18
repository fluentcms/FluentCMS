using FluentCMS.Core.Schema;

namespace FluentCMS.Infrastructure.Content;

// One reference one version of an entry makes, written with the version and never changed (CNT-03, ADR-0005).
public sealed class EntryReferenceRow
{
    public Guid Id { get; set; }

    public Guid EntryId { get; set; }

    public int Version { get; set; }

    // The JSON Pointer of the reference in the version's fields, such as /prices/2.
    public string Path { get; set; } = string.Empty;

    public ReferenceKind TargetKind { get; set; }

    public Guid TargetId { get; set; }

    // The entity field referenced, such as price; null for a reference to a whole entry or to a media asset.
    public string? TargetField { get; set; }
}
