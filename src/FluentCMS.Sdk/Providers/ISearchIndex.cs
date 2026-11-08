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
    /// </returns>
    Task<IReadOnlyList<Guid>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default);
}
