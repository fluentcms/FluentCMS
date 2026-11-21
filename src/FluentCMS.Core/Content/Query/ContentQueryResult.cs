using FluentCMS.Content;

namespace FluentCMS.Core.Content.Query;

// Page is null for a page read by offset or cursor. NextCursor is null on the last page, however the page was read.
// SearchTruncated is true when the search index returned the most ids the query asks it for.
internal sealed record ContentQueryResult(IReadOnlyList<ContentDocument> Items, long? Total, int? Page, int PageSize, string? NextCursor, bool SearchTruncated = false);
