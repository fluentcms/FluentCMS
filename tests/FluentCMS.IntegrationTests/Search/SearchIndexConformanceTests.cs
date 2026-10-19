using FluentCMS.Core.Persistence;
using FluentCMS.Core.Search;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Search;

// The contract every search index meets, built in or not (EXT-N1, ADR-0010), on each database, since an index writes inside the
// write's unit of work. Derive one class per index and register it in AddIndex; the built-in ones come with their provider.
[ConformanceFor(typeof(ISearchIndex))]
public abstract class SearchIndexConformanceTests
{
    protected virtual void AddIndex(IServiceCollection services)
    {
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_document_is_found_by_the_start_of_each_word_and_only_with_every_word(string provider)
    {
        await using var index = await IndexScenario.CreateAsync(provider, AddIndex);
        var widget = await index.AddAsync("widget pro a 4411");
        var gadget = await index.AddAsync("gadget 4412");

        Assert.Equal([widget], await index.MatchAsync("441", "wid"));
        Assert.Equal(new[] { widget, gadget }.Order(), (await index.MatchAsync("441")).Order());
        Assert.Empty(await index.MatchAsync("widget", "gadget"));
        Assert.Empty(await index.MatchAsync("idget"));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_document_indexed_again_is_found_by_its_new_words_only(string provider)
    {
        await using var index = await IndexScenario.CreateAsync(provider, AddIndex);
        var entry = await index.AddAsync("first draft borealis");

        await index.AddAsync("second draft australis", entry);

        Assert.Empty(await index.MatchAsync("borealis"));
        Assert.Equal([entry], await index.MatchAsync("australis"));
        Assert.Equal([entry], await index.MatchAsync("draft"));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_document_indexed_in_a_unit_of_work_that_rolls_back_is_not_found(string provider)
    {
        await using var index = await IndexScenario.CreateAsync(provider, AddIndex);
        var kept = await index.AddAsync("kept aurora");

        await index.AddAsync("rolled back aurora", commit: false);
        await index.AddAsync("rolled back aurora again", kept, commit: false);

        Assert.Equal([kept], await index.MatchAsync("aurora"));
        Assert.Empty(await index.MatchAsync("rolled"));
    }

    // A caller pages through matches by skipping those it read, so pages read in turn are one read cut into pieces.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Pages_read_in_turn_are_one_read_best_first_cut_into_pieces(string provider)
    {
        await using var index = await IndexScenario.CreateAsync(provider, AddIndex);
        var strongest = await index.AddAsync("zephyr zephyr zephyr zephyr");
        foreach (var words in Enumerable.Range(0, 24).Select(other => $"zephyr item {other}"))
        {
            await index.AddAsync(words);
        }

        var all = await index.MatchesAsync(["zephyr"], 0, 100);
        var pages = new List<SearchMatch>();
        for (var skip = 0; skip < all.Count; skip += 10)
        {
            pages.AddRange(await index.MatchesAsync(["zephyr"], skip, 10));
        }

        Assert.Equal(25, all.Count);
        Assert.Equal(strongest, all[0].EntryId);
        Assert.Equal(all.Select(match => match.EntryId), pages.Select(match => match.EntryId));
        Assert.True(all.Zip(all.Skip(1)).All(pair => pair.First.Rank >= pair.Second.Rank), "Matches are not best first.");
        Assert.Empty(await index.MatchesAsync(["zephyr"], 25, 10));
    }

    private sealed class IndexScenario(TemporaryDatabase database, ServiceProvider services) : IAsyncDisposable
    {
        public static async Task<IndexScenario> CreateAsync(string provider, Action<IServiceCollection> addIndex)
        {
            var database = await TemporaryDatabase.CreateAsync(provider);
            var services = TestServices.Build(
                database.Configuration,
                collection => collection.AddSingleton<Core.Identity.ICurrentInitiator>(new TestInitiator(null)),
                addIndex);
            await TestServices.MigrateAsync(services);
            return new IndexScenario(database, services);
        }

        // Indexes the text for a new entry id, or again for the given one, in a unit of work that commits unless told not to.
        public async Task<Guid> AddAsync(string text, Guid? entry = null, bool commit = true)
        {
            var id = entry ?? Guid.CreateVersion7();
            await using var scope = services.CreateAsyncScope();
            await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync();
            await scope.ServiceProvider.GetRequiredService<ISearchIndex>().IndexAsync(new SearchDocument(id, text));
            if (commit)
            {
                await work.CommitAsync();
            }

            return id;
        }

        public async Task<List<Guid>> MatchAsync(params string[] terms)
        {
            return [.. (await MatchesAsync(terms, 0, 100)).Select(match => match.EntryId)];
        }

        public async Task<IReadOnlyList<SearchMatch>> MatchesAsync(string[] terms, int skip, int limit)
        {
            await using var scope = services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ISearchIndex>().MatchAsync(terms, skip, limit);
        }

        public async ValueTask DisposeAsync()
        {
            await services.DisposeAsync();
            await database.DisposeAsync();
        }
    }
}

// The index each provider comes with: tsvector and GIN on PostgreSQL, FTS5 on SQLite.
public sealed class InDatabaseSearchIndexConformanceTests : SearchIndexConformanceTests;
