namespace FluentCMS.Content;

/// <summary>What <see cref="IContentService.BulkAsync"/> did, entry by entry.</summary>
/// <param name="Items">One result for each entry, in the order of the request or of the selection.</param>
/// <param name="Cancelled">Whether the request was cancelled. The entries that ran before stay as they are.</param>
public sealed record BulkResult(IReadOnlyList<BulkItemResult> Items, bool Cancelled);
