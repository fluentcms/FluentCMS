using FluentCMS.Content;
using FluentCMS.Sites;

namespace FluentCMS.Providers;

/// <summary>A search that the query engine asks of an <see cref="ISearchIndex"/> for one list query.</summary>
/// <param name="Type">The content type of the query.</param>
/// <param name="Text">The search text as the client sent it.</param>
/// <param name="Limit">
/// The most ids to return; the engine reads no more than these, and a list for which the index returns this many says its
/// search may be cut.
/// </param>
public sealed record SearchRequest(string Type, string Text, int Limit)
{
    /// <summary>The locale the query reads, or empty for a type that is not localized.</summary>
    public string Locale { get; init; } = "";

    /// <summary>The site the query reads, or <see langword="null"/> for every site.</summary>
    public SiteId? Site { get; init; }

    /// <summary>The state the query reads.</summary>
    public QueryStatus Status { get; init; }

    /// <summary>
    /// The searchable fields the caller may read, which alone may match, or <see langword="null"/> when the caller may
    /// read every one of them. Only an index whose <see cref="ISearchIndex.MatchesByField"/> is true is given a list.
    /// </summary>
    public IReadOnlyList<string>? Fields { get; init; }
}
