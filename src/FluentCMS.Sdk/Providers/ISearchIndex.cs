namespace FluentCMS.Providers;

/// <summary>
/// Finds the entries that match a search text, for <see cref="Content.ContentQuery.Search"/>. Core has no index of its
/// own: until the search plugin or the host fills this slot, a query with a search is refused. The index decides how it
/// keeps up with content and which fields it reads, such as those marked <see cref="Schema.SearchableAttribute"/>.
/// </summary>
public interface ISearchIndex
{
    /// <summary>Finds the entries of one content type that match a search text.</summary>
    /// <param name="request">The search and the type, locale, site and state of the query that asks for it.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>
    /// The document ids of the best matches, at most <see cref="SearchRequest.Limit"/>. The query is narrowed to them and
    /// keeps its own filter, row rule, site, locale, status and sort, so an id the caller may not read is never returned.
    /// The ids are to be of the request's type, locale, site and state only: another's would take the place of an entry the
    /// caller may read. When the index returns <see cref="SearchRequest.Limit"/> ids, the list says that its search may be
    /// cut (<see cref="Content.PagedResult{T}.SearchTruncated"/>); an index that stops short of the limit on its own while
    /// more entries match is not reported as cut.
    /// </returns>
    Task<IReadOnlyList<Guid>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the index matches only the fields of <see cref="SearchRequest.Fields"/> when a request names them. The engine
    /// refuses a search to a caller who may not read every searchable field unless the index says it does. A request whose
    /// list is null does not ask for every row of the index but for the searchable fields that are not hidden, so an index
    /// that holds rows of another field, such as a hidden one, never matches them for any caller.
    /// </summary>
    bool MatchesByField => false;
}
