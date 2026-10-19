using FluentCMS.Core.Agents;
using FluentCMS.Core.Content;
using FluentCMS.Core.Results;
using FluentCMS.Core.Search;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Content;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FluentCMS.Infrastructure.Search;

// The index finds the text; the entry rows decide the rest, so deleting, restoring and publishing change results without touching
// the index. A match is followed to the entries whose latest version references it, such as the page that shows a matching entity.
internal sealed class EntrySearch(FluentCmsDbContext context, ISearchIndex index, IAgentPermissions permissions, IOptions<SearchOptions> options) : ISearch
{
    private readonly SearchOptions _settings = options.Value;

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
        var (matched, ranks, complete) = await MatchedAsync(terms, readable, query, cancellationToken);
        var direct = matched
            .Where(entry => Passes(entry, query))
            .Select(entry => entry.Hit(ranks[entry.Id], via: null))
            .OrderByDescending(hit => hit.Rank)
            .ThenBy(hit => hit.EntryId)
            .Take(query.Limit)
            .ToList();

        var room = query.Limit - direct.Count;
        if (room == 0)
        {
            return Result<SearchResults>.Success(new SearchResults(direct, complete));
        }

        // Only matches the caller may read lead anywhere, so a reference never reveals an entry of a type outside the scope.
        var targets = matched.ToDictionary(entry => entry.Id, entry => ranks[entry.Id]);
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
            .Where(entry => !shown.Contains(entry.Id) && readable(entry.ContentType) && Passes(entry, query))
            .Select(entry =>
            {
                // The best match the entry references explains why it is shown.
                var target = references
                    .Where(reference => reference.EntryId == entry.Id)
                    .Select(reference => reference.TargetId)
                    .OrderByDescending(id => targets[id])
                    .ThenBy(id => id)
                    .First();
                return entry.Hit(targets[target], target);
            })
            .OrderByDescending(hit => hit.Rank)
            .ThenBy(hit => hit.EntryId)
            .Take(room);

        return Result<SearchResults>.Success(new SearchResults([.. direct, .. via], complete));
    }

    // The readable matches, a page at a time in rank order. Better-ranked matches the scope, deletions or filters drop would otherwise
    // crowd out the rest, so reading stops only once enough pass the filters, the index has no more, or MaxMatches are read.
    private async Task<(List<Candidate> Matched, Dictionary<Guid, double> Ranks, bool Complete)> MatchedAsync(
        IReadOnlyList<string> terms,
        Func<string, bool> readable,
        SearchQuery query,
        CancellationToken cancellationToken)
    {
        var matched = new List<Candidate>();
        var ranks = new Dictionary<Guid, double>();
        var read = 0;

        while (read < _settings.MaxMatches)
        {
            var take = Math.Min(_settings.PageSize, _settings.MaxMatches - read);
            var page = await index.MatchAsync(terms, read, take, cancellationToken);
            read += page.Count;

            var fresh = page.Where(match => ranks.TryAdd(match.EntryId, match.Rank)).Select(match => match.EntryId).ToList();
            matched.AddRange((await EntriesAsync(context.Entries.Where(row => fresh.Contains(row.Id)), cancellationToken))
                .Where(entry => readable(entry.ContentType)));

            if (page.Count < take || matched.Count(entry => Passes(entry, query)) >= query.Limit)
            {
                return (matched, ranks, true);
            }
        }

        // Whether anything is left past the last match read.
        var more = await index.MatchAsync(terms, read, 1, cancellationToken);
        return (matched, ranks, more.Count == 0);
    }

    // In review is read from the change sets holding the entry, as the entry tools read it (CNT-01).
    private async Task<List<Candidate>> EntriesAsync(IQueryable<EntryRow> rows, CancellationToken cancellationToken)
    {
        var siteId = Site.Default.Id;
        var inReview = EntriesInReview.Ids(context);
        var found = await rows
            .AsNoTracking()
            .Where(row => row.SiteId == siteId && row.DeletedAt == null)
            .Join(context.ContentTypes, row => row.ContentTypeId, type => type.Id, (row, type) => new { row, type.Slug })
            .Select(pair => new
            {
                pair.row.Id,
                pair.Slug,
                pair.row.State,
                pair.row.LatestVersion,
                pair.row.PublishedVersion,
                InReview = inReview.Contains(pair.row.Id),
            })
            .ToListAsync(cancellationToken);

        return [.. found.Select(entry => new Candidate(
            entry.Id,
            entry.Slug,
            entry.InReview ? EntryState.InReview : entry.State,
            entry.LatestVersion,
            entry.PublishedVersion))];
    }

    private static bool Passes(Candidate entry, SearchQuery query)
    {
        return (query.ContentTypes is null || query.ContentTypes.Contains(entry.ContentType, StringComparer.Ordinal))
            && (query.States is null || query.States.Contains(entry.State))
            && (!query.PublishedOnly || entry.PublishedVersion is not null);
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

        if (query.ContentTypes is { Count: 0 })
        {
            errors.Add(new Error(SearchErrorCodes.FilterEmpty, "The types filter is empty; name at least one content type, or leave it out to search every type you may read."));
        }

        if (query.States is { Count: 0 })
        {
            errors.Add(new Error(SearchErrorCodes.FilterEmpty, "The states filter is empty; name at least one state, or leave it out to keep every state."));
        }

        if (query.Limit is < 1 or > SearchQuery.MaxLimit)
        {
            errors.Add(new Error(SearchErrorCodes.LimitInvalid, $"The limit is from 1 to {SearchQuery.MaxLimit}, and {query.Limit} was given."));
        }

        return errors;
    }

    private sealed record Candidate(Guid Id, string ContentType, EntryState State, int LatestVersion, int? PublishedVersion)
    {
        public SearchHit Hit(double rank, Guid? via)
        {
            return new SearchHit(Id, ContentType, State, LatestVersion, PublishedVersion, rank, via);
        }
    }
}
