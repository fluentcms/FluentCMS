using FluentCMS.Core.Content;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Search;

// Finding entries and entities by text with filters (SRC-01), for the search tool and the desk. Only the types the caller may read
// are searched, so search never shows more than the read tools would (MCP-05).
public interface ISearch
{
    Task<Result<SearchResults>> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default);
}

public sealed record SearchQuery(string Text)
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    // Content type slugs; null for every type.
    public IReadOnlyList<string>? ContentTypes { get; init; }

    // Null for every state.
    public IReadOnlyList<EntryState>? States { get; init; }

    public bool PublishedOnly { get; init; }

    public int Limit { get; init; } = DefaultLimit;
}

public sealed record SearchResults(IReadOnlyList<SearchHit> Hits);

// A direct match has no Via; an entry that references a match names it in Via, such as the page that shows a matching entity.
public sealed record SearchHit(
    Guid EntryId,
    string ContentType,
    EntryState State,
    int LatestVersion,
    int? PublishedVersion,
    double Rank,
    Guid? Via);

public static class SearchErrorCodes
{
    public const string QueryEmpty = "search_query_empty";
    public const string QueryTooLong = "search_query_too_long";
    public const string LimitInvalid = "search_limit_invalid";
    public const string FilterEmpty = "search_filter_empty";
}
