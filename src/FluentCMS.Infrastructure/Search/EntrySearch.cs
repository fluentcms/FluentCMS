using System.Text.Json;
using FluentCMS.Core.Agents;
using FluentCMS.Core.Content;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Search;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Content;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FluentCMS.Infrastructure.Search;

// The index finds the text; the entry rows decide the rest, so deleting, restoring and publishing change results without touching
// the index. A match is followed to the entries whose latest version references it, such as the page that shows a matching entity.
// Field and reference filters read the latest versions of the candidates that pass everything else.
internal sealed class EntrySearch(
    FluentCmsDbContext context,
    ISearchIndex index,
    IAgentPermissions permissions,
    IContentTypeDirectory contentTypes,
    IOptions<SearchOptions> options) : ISearch
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
        var types = await CheckTypesAsync(query.ContentTypes, readable, cancellationToken);
        if (types.Count > 0)
        {
            return Result<SearchResults>.Failure(types);
        }

        var filters = await FieldFiltersAsync(query, cancellationToken);
        if (!filters.IsSuccess)
        {
            return Result<SearchResults>.Failure(filters.Errors);
        }

        var (matched, passing, ranks, complete) = await MatchedAsync(terms, readable, query, filters.Value, cancellationToken);
        var direct = matched
            .Where(entry => passing.Contains(entry.Id))
            .Select(entry => entry.Hit(ranks[entry.Id], via: null))
            .OrderByDescending(hit => hit.Rank)
            .ThenBy(hit => hit.EntryId)
            .Take(query.Limit)
            .ToList();

        // Without words every candidate is a match, so there is nothing for a reference to explain.
        var room = query.Limit - direct.Count;
        if (room == 0 || terms.Count == 0)
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
        var referencing = (await EntriesAsync(context.Entries.Where(row => referencingIds.Contains(row.Id)), cancellationToken))
            .Where(entry => !shown.Contains(entry.Id) && readable(entry.ContentType) && Passes(entry, query))
            .ToList();
        var referencingPassing = await StructuredAsync(referencing, query, filters.Value, cancellationToken);
        var via = referencing
            .Where(entry => referencingPassing.Contains(entry.Id))
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
    private async Task<(List<Candidate> Matched, HashSet<Guid> Passing, Dictionary<Guid, double> Ranks, bool Complete)> MatchedAsync(
        IReadOnlyList<string> terms,
        Func<string, bool> readable,
        SearchQuery query,
        FieldFilters filters,
        CancellationToken cancellationToken)
    {
        var matched = new List<Candidate>();
        var passing = new HashSet<Guid>();
        var ranks = new Dictionary<Guid, double>();
        var read = 0;

        while (read < _settings.MaxMatches)
        {
            var take = Math.Min(_settings.PageSize, _settings.MaxMatches - read);
            var page = await PageAsync(terms, query, read, take, cancellationToken);
            read += page.Count;

            var fresh = page.Where(match => ranks.TryAdd(match.EntryId, match.Rank)).Select(match => match.EntryId).ToList();
            var candidates = (await EntriesAsync(context.Entries.Where(row => fresh.Contains(row.Id)), cancellationToken))
                .Where(entry => readable(entry.ContentType))
                .ToList();
            matched.AddRange(candidates);
            passing.UnionWith(await StructuredAsync([.. candidates.Where(entry => Passes(entry, query))], query, filters, cancellationToken));

            if (page.Count < take || passing.Count >= query.Limit)
            {
                return (matched, passing, ranks, true);
            }
        }

        // Whether anything is left past the last match read.
        var more = await PageAsync(terms, query, read, 1, cancellationToken);
        return (matched, passing, ranks, more.Count == 0);
    }

    // Matches of the words in rank order, or without words the one type's entries in id order, each with no rank.
    private async Task<IReadOnlyList<SearchMatch>> PageAsync(IReadOnlyList<string> terms, SearchQuery query, int skip, int take, CancellationToken cancellationToken)
    {
        if (terms.Count > 0)
        {
            return await index.MatchAsync(terms, skip, take, cancellationToken);
        }

        var siteId = Site.Default.Id;
        var slug = query.ContentTypes![0];
        var ids = await context.Entries
            .AsNoTracking()
            .Join(context.ContentTypes, row => row.ContentTypeId, type => type.Id, (row, type) => new { row, type.Slug })
            .Where(pair => pair.row.SiteId == siteId && pair.Slug == slug && pair.row.DeletedAt == null)
            .OrderBy(pair => pair.row.Id)
            .Skip(skip)
            .Take(take)
            .Select(pair => pair.row.Id)
            .ToListAsync(cancellationToken);

        return [.. ids.Select(id => new SearchMatch(id, 0))];
    }

    // The field filters checked against the one type they need, before anything is read.
    private async Task<Result<FieldFilters>> FieldFiltersAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        if (query.Fields is not { Count: > 0 } fields)
        {
            return Result<FieldFilters>.Success(FieldFilters.None);
        }

        var slug = query.ContentTypes![0];
        var type = await contentTypes.GetAsync(slug, cancellationToken);

        return type.IsSuccess ? FieldFilters.Create(fields, slug, type.Value.Definition) : Result<FieldFilters>.Failure(type.Errors);
    }

    // The candidates whose latest version references the given id and has field values the filters accept; every candidate when the
    // query has neither.
    private async Task<HashSet<Guid>> StructuredAsync(IReadOnlyList<Candidate> candidates, SearchQuery query, FieldFilters filters, CancellationToken cancellationToken)
    {
        var ids = candidates.Select(entry => entry.Id).ToList();
        if (ids.Count == 0 || !IsStructured(query))
        {
            return [.. ids];
        }

        if (query.References is { } target)
        {
            ids = await context.EntryReferences
                .AsNoTracking()
                .Where(reference => ids.Contains(reference.EntryId) && reference.TargetId == target)
                .Join(context.Entries, reference => reference.EntryId, row => row.Id, (reference, row) => new { reference, row })
                .Where(pair => pair.reference.Version == pair.row.LatestVersion)
                .Select(pair => pair.reference.EntryId)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        if (query.Fields is { Count: > 0 } && ids.Count > 0)
        {
            var versions = await context.EntryVersions
                .AsNoTracking()
                .Join(context.Entries, version => version.EntryId, row => row.Id, (version, row) => new { version, row })
                .Where(pair => ids.Contains(pair.row.Id) && pair.version.Version == pair.row.LatestVersion)
                .Select(pair => new { pair.row.Id, pair.version.Fields })
                .ToListAsync(cancellationToken);

            ids = [.. versions.Where(version => Accepts(filters, version.Fields)).Select(version => version.Id)];
        }

        return [.. ids];
    }

    private static bool Accepts(FieldFilters filters, string fields)
    {
        using var document = JsonDocument.Parse(fields);
        return filters.Matches(document.RootElement);
    }

    private static bool IsStructured(SearchQuery query)
    {
        return query.References is not null || query.Fields is { Count: > 0 };
    }

    // Each type a filter names exists and is one the caller may read, as list_entries asks, so a typo or a type outside the scope is
    // not taken for a search that matched nothing.
    private async Task<List<Error>> CheckTypesAsync(IReadOnlyList<string>? slugs, Func<string, bool> readable, CancellationToken cancellationToken)
    {
        if (slugs is not { Count: > 0 })
        {
            return [];
        }

        var siteId = Site.Default.Id;
        var known = await context.ContentTypes
            .AsNoTracking()
            .Where(type => type.SiteId == siteId && slugs.Contains(type.Slug))
            .Select(type => type.Slug)
            .ToListAsync(cancellationToken);

        var errors = new List<Error>();
        foreach (var slug in slugs.Distinct(StringComparer.Ordinal))
        {
            if (!known.Contains(slug, StringComparer.Ordinal))
            {
                errors.Add(new Error(SchemaErrorCodes.ContentTypeNotFound, $"There is no content type '{slug}' in this site; list_content_types shows the types there are."));
            }
            else if (!readable(slug))
            {
                errors.Add(new Error(AgentPermissionErrorCodes.ScopeDenied, $"'{slug}' is not among the content types this credential may read, so it cannot be searched."));
            }
        }

        return errors;
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

        if (terms.Count == 0 && !IsStructured(query))
        {
            errors.Add(new Error(
                SearchErrorCodes.QueryEmpty,
                "The query has no words to search for; give at least one word or number, such as '4411', or filter one type by its fields or a reference."));
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

        if (query.Fields is { Count: 0 })
        {
            errors.Add(new Error(SearchErrorCodes.FilterEmpty, "The fields filter is empty; give at least one field filter, or leave it out."));
        }

        // A field belongs to one type, and without words the candidates are one type's entries.
        var needsOneType = query.Fields is { Count: > 0 } || (terms.Count == 0 && IsStructured(query));
        if (needsOneType && query.ContentTypes is not { Count: 0 } && query.ContentTypes?.Distinct(StringComparer.Ordinal).Count() != 1)
        {
            errors.Add(new Error(
                SearchErrorCodes.FilterNeedsOneType,
                terms.Count == 0
                    ? "A search without words needs exactly one type in types, such as [\"product\"], whose entries it filters."
                    : "Field filters need exactly one type in types, such as [\"product\"], whose fields they name."));
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
