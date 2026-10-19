using FluentCMS.Core.Agents;
using FluentCMS.Core.Content;
using FluentCMS.Core.Results;
using FluentCMS.Core.Search;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Content;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Search;

// The index finds the text; the entry rows decide the rest, so deleting, restoring and publishing change results without touching
// the index. A match is followed to the entries whose latest version references it, such as the page that shows a matching entity.
internal sealed class EntrySearch(FluentCmsDbContext context, ISearchIndex index, IAgentPermissions permissions) : ISearch
{
    // Matches read before filtering, so filters that drop many still leave enough to fill the limit.
    private const int CandidatesPerHit = 10;
    private const int MaxCandidates = 1000;

    public async Task<Result<SearchResults>> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var terms = SearchTerms.From(query.Text);
        var invalid = Validate(query, terms);
        if (invalid.Count > 0)
        {
            return Result<SearchResults>.Failure(invalid);
        }

        var readable = await permissions.ReadableTypesAsync(cancellationToken);
        // A filter can drop most matches, so a filtered search reads as many as it may.
        var filtered = query.ContentTypes is not null || query.States is not null || query.PublishedOnly;
        var candidates = filtered ? MaxCandidates : Math.Min(query.Limit * CandidatesPerHit, MaxCandidates);
        var matches = await index.MatchAsync(terms, candidates, cancellationToken);
        var ranks = matches.ToDictionary(match => match.EntryId, match => match.Rank);
        var matchedIds = ranks.Keys.ToList();

        var matched = (await EntriesAsync(context.Entries.Where(row => matchedIds.Contains(row.Id)), cancellationToken))
            .Where(entry => readable(entry.ContentType))
            .ToList();
        var direct = matched
            .Where(entry => Passes(entry, query))
            .Select(entry => entry.Hit(ranks[entry.Row.Id], via: null))
            .OrderByDescending(hit => hit.Rank)
            .ThenBy(hit => hit.EntryId)
            .Take(query.Limit)
            .ToList();

        var room = query.Limit - direct.Count;
        if (room == 0)
        {
            return Result<SearchResults>.Success(new SearchResults(direct));
        }

        // Only matches the caller may read lead anywhere, so a reference never reveals an entry of a type outside the scope.
        var targets = matched.ToDictionary(entry => entry.Row.Id, entry => ranks[entry.Row.Id]);
        var targetIds = targets.Keys.ToList();
        var references = await context.EntryReferences
            .AsNoTracking()
            .Where(reference => targetIds.Contains(reference.TargetId))
            .Join(context.Entries, reference => reference.EntryId, row => row.Id, (reference, row) => new { reference, row })
            .Where(pair => pair.reference.Version == pair.row.LatestVersion)
            .Select(pair => new { pair.reference.EntryId, pair.reference.TargetId })
            .ToListAsync(cancellationToken);

        var referencingIds = references.Select(reference => reference.EntryId).Distinct().ToList();
        var shown = direct.Select(hit => hit.EntryId).ToHashSet();
        var via = (await EntriesAsync(context.Entries.Where(row => referencingIds.Contains(row.Id)), cancellationToken))
            .Where(entry => !shown.Contains(entry.Row.Id) && readable(entry.ContentType) && Passes(entry, query))
            .Select(entry =>
            {
                // The best match the entry references explains why it is shown.
                var target = references
                    .Where(reference => reference.EntryId == entry.Row.Id)
                    .Select(reference => reference.TargetId)
                    .OrderByDescending(id => targets[id])
                    .ThenBy(id => id)
                    .First();
                return entry.Hit(targets[target], target);
            })
            .OrderByDescending(hit => hit.Rank)
            .ThenBy(hit => hit.EntryId)
            .Take(room);

        return Result<SearchResults>.Success(new SearchResults([.. direct, .. via]));
    }

    private async Task<List<Candidate>> EntriesAsync(IQueryable<EntryRow> rows, CancellationToken cancellationToken)
    {
        var siteId = Site.Default.Id;
        var found = await rows
            .AsNoTracking()
            .Where(row => row.SiteId == siteId && row.DeletedAt == null)
            .Join(context.ContentTypes, row => row.ContentTypeId, type => type.Id, (row, type) => new { row, type.Slug })
            .ToListAsync(cancellationToken);

        return [.. found.Select(pair => new Candidate(pair.row, pair.Slug))];
    }

    private static bool Passes(Candidate entry, SearchQuery query)
    {
        return (query.ContentTypes is null || query.ContentTypes.Contains(entry.ContentType, StringComparer.Ordinal))
            && (query.States is null || query.States.Contains(entry.Row.State))
            && (!query.PublishedOnly || entry.Row.PublishedVersion is not null);
    }

    private static List<Error> Validate(SearchQuery query, IReadOnlyList<string> terms)
    {
        var errors = new List<Error>();

        if (terms.Count == 0)
        {
            errors.Add(new Error(SearchErrorCodes.QueryEmpty, "The query has no words to search for; give at least one word or number, such as '4411'."));
        }
        else if (terms.Count > SearchTerms.MaxTerms || terms.Any(term => term.Length > SearchTerms.MaxTermLength))
        {
            errors.Add(new Error(
                SearchErrorCodes.QueryTooLong,
                $"A query has at most {SearchTerms.MaxTerms} words of at most {SearchTerms.MaxTermLength} characters each; search for fewer, shorter words."));
        }

        if (query.Limit is < 1 or > SearchQuery.MaxLimit)
        {
            errors.Add(new Error(SearchErrorCodes.LimitInvalid, $"The limit is from 1 to {SearchQuery.MaxLimit}, and {query.Limit} was given."));
        }

        return errors;
    }

    private sealed record Candidate(EntryRow Row, string ContentType)
    {
        public SearchHit Hit(double rank, Guid? via)
        {
            return new SearchHit(Row.Id, ContentType, Row.State, Row.LatestVersion, Row.PublishedVersion, rank, via);
        }
    }
}
