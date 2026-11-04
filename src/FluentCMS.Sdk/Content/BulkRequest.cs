namespace FluentCMS.Content;

/// <summary>
/// What <see cref="IContentService.BulkAsync"/> does: one operation on many entries, each run as the single call would run it,
/// in a transaction of its own. <see cref="BulkOperation.Create"/> takes <see cref="Entries"/>; every other operation takes
/// either <see cref="Items"/> or a <see cref="Filter"/> with a <see cref="Cap"/>.
/// </summary>
public sealed record BulkRequest
{
    /// <summary>The most entries one request may name, create or select.</summary>
    public const int MaxItems = 1000;

    /// <summary>What to do to each entry.</summary>
    public required BulkOperation Operation { get; init; }

    /// <summary>The values of the entries to create, for <see cref="BulkOperation.Create"/> only.</summary>
    public IReadOnlyList<ContentDocument>? Entries { get; init; }

    /// <summary>The entries to act on by id. An id named again is run once, where it first appears.</summary>
    public IReadOnlyList<BulkItem>? Items { get; init; }

    /// <summary>
    /// The entries to act on by filter, read against the drafts in the locale of the request. It selects only entries the
    /// principal may read and may also perform the operation on, and a filter on a relation matches only targets it may read.
    /// An update or a publish acts on the version the filter read, so a draft changed since fails with a conflict; an
    /// unpublish or a delete acts on what matched when the filter was read, and both can be undone.
    /// </summary>
    public ContentFilter? Filter { get; init; }

    /// <summary>
    /// The most entries a <see cref="Filter"/> may select, at most <see cref="MaxItems"/>. Required with a filter: a filter
    /// that selects more is refused before anything is written.
    /// </summary>
    public int? Cap { get; init; }

    /// <summary>The JSON merge patch applied to every entry a <see cref="Filter"/> selects, for <see cref="BulkOperation.Update"/> only. Each entry is changed at the version the filter read.</summary>
    public ContentDocument? Patch { get; init; }

    /// <summary>The locale to publish or unpublish, or <see langword="null"/> for that of the operation. For <see cref="BulkOperation.Publish"/> and <see cref="BulkOperation.Unpublish"/> only.</summary>
    public string? Locale { get; init; }
}
