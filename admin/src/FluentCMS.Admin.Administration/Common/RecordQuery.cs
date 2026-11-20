namespace FluentCMS.Admin.Administration.Common;

// What a list of records asks its page to read: the search of the address, if any, and the page, from 1.
public sealed record RecordQuery(string? Search, int Page, int PageSize);

// A page of records as the server answers it, with the number of records the search finds in all.
public sealed record RecordPage<TItem>(IReadOnlyList<TItem> Items, long Total);
