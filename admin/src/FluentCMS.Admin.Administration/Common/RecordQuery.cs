namespace FluentCMS.Admin.Administration.Common;

// What a list of records asks its page to read: the search of the address, if any, the page, from 1, and the values the
// address gives the page's own filters, by the name of their query parameter; a filter the address does not set is absent.
public sealed record RecordQuery(string? Search, int Page, int PageSize)
{
    public IReadOnlyDictionary<string, string> Filters { get; init; } = new Dictionary<string, string>();

    public string? Filter(string name) => Filters.GetValueOrDefault(name);

    // Two queries with the same filters are one read, whatever dictionary holds them.
    public bool Equals(RecordQuery? other) =>
        other is not null && Search == other.Search && Page == other.Page && PageSize == other.PageSize
        && Filters.Count == other.Filters.Count && Filters.All(filter => other.Filters.TryGetValue(filter.Key, out var value) && value == filter.Value);

    public override int GetHashCode() => HashCode.Combine(Search, Page, PageSize, Filters.Count);
}

// A page of records as the server answers it, with the number of records the search finds in all.
public sealed record RecordPage<TItem>(IReadOnlyList<TItem> Items, long Total);
