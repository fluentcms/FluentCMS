using System.Text.Json;
using FluentCMS.Core.Content;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Search;

// Finding entries and entities by text with filters (SRC-01), for the search tool and the desk. Only the types the caller may read
// are searched, so search never shows more than the read tools would (MCP-05).
public interface ISearch
{
    Task<Result<SearchResults>> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default);
}

// Text is optional when the query names one content type and filters it by field values or a reference.
public sealed record SearchQuery(string Text)
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    // Content type slugs; null for every type.
    public IReadOnlyList<string>? ContentTypes { get; init; }

    // Null for every state.
    public IReadOnlyList<EntryState>? States { get; init; }

    public bool PublishedOnly { get; init; }

    // On the latest version's field values, all of which must hold; they need exactly one content type in ContentTypes.
    public IReadOnlyList<FieldFilter>? Fields { get; init; }

    // An entry or entity id: only entries whose latest version references it.
    public Guid? References { get; init; }

    public int Limit { get; init; } = DefaultLimit;
}

// On an entity, the field's fact value is compared.
public sealed record FieldFilter(string Field, FieldOperator Operator, JsonElement Value);

public enum FieldOperator
{
    EqualTo,
    Contains,
    LessThan,
    AtMost,
    GreaterThan,
    AtLeast,
}

// Complete is false when the search stopped at the most matches it reads, so better-ranked matches outside the filters or the
// scope may have crowded out some that would pass them.
public sealed record SearchResults(IReadOnlyList<SearchHit> Hits, bool Complete = true);

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
    public const string FilterNeedsOneType = "search_filter_needs_one_type";
    public const string FieldNotFound = "search_field_not_found";
    public const string FieldNotFilterable = "search_field_not_filterable";
    public const string OperatorNotSupported = "search_operator_not_supported";
    public const string FilterValueInvalid = "search_filter_value_invalid";
}
