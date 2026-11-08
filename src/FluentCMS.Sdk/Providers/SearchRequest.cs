using FluentCMS.Content;
using FluentCMS.Sites;

namespace FluentCMS.Providers;

/// <summary>A search that the query engine asks of an <see cref="ISearchIndex"/> for one list query.</summary>
/// <param name="Type">The content type of the query.</param>
/// <param name="Text">The search text as the client sent it.</param>
/// <param name="Limit">The most ids to return; the engine reads no more than these.</param>
public sealed record SearchRequest(string Type, string Text, int Limit)
{
    /// <summary>The locale the query reads, or empty for a type that is not localized.</summary>
    public string Locale { get; init; } = "";

    /// <summary>The site the query reads, or <see langword="null"/> for every site.</summary>
    public SiteId? Site { get; init; }

    /// <summary>The state the query reads.</summary>
    public QueryStatus Status { get; init; }
}
