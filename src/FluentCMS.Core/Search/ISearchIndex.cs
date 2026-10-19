using FluentCMS.Core.Extensions;

namespace FluentCMS.Core.Search;

// The full-text index over entries and entities (SRC-01, ADR-0010): PostgreSQL full-text search or SQLite FTS5 in the application
// database, or an external engine a plugin adds later. IndexAsync is called inside the unit of work of the write it reflects, so an
// entry is found the moment its write commits and never when the write rolled back.
[ExtensionPoint]
public interface ISearchIndex
{
    // Replaces what the index holds for the entry.
    Task IndexAsync(SearchDocument document, CancellationToken cancellationToken = default);

    // Entries whose text has every term, each matched as a prefix, best first. Terms are words of letters and digits (SearchTerms).
    Task<IReadOnlyList<SearchMatch>> MatchAsync(IReadOnlyList<string> terms, int limit, CancellationToken cancellationToken = default);
}

public sealed record SearchDocument(Guid EntryId, string Text);

public sealed record SearchMatch(Guid EntryId, double Rank);
