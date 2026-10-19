using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Search;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.IntegrationTests.ChangeSets;
using FluentCMS.IntegrationTests.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.IntegrationTests.Content.EntryScenario;
using static FluentCMS.IntegrationTests.Schema.SchemaScenario;

namespace FluentCMS.IntegrationTests.Search;

public sealed class SearchTests
{
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_sku_finds_the_entity_and_the_page_that_references_it_and_filters_narrow_the_result(string provider)
    {
        await using var scenario = await CatalogAsync(provider);
        var (product, page, other) = (scenario.Product, scenario.Page, scenario.Other);

        var all = await SearchAsync(scenario.Entries, new SearchQuery("4411"));
        var prefix = await SearchAsync(scenario.Entries, new SearchQuery("441"));
        var pagesOnly = await SearchAsync(scenario.Entries, new SearchQuery("4411") { ContentTypes = ["page"] });
        var twoWords = await SearchAsync(scenario.Entries, new SearchQuery("widget 4411"));
        var publishedBefore = await SearchAsync(scenario.Entries, new SearchQuery("4411") { PublishedOnly = true });
        await scenario.Entries.Schema.QueryAsync(context => context.Entries.Where(row => row.Id == product).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PublishedVersion, 1)));
        var publishedAfter = await SearchAsync(scenario.Entries, new SearchQuery("4411") { PublishedOnly = true });

        Assert.Equal([(product, (Guid?)null), (page, product)], all.Select(hit => (hit.EntryId, hit.Via)));
        Assert.Equal(("product", EntryState.Draft, 1), (all[0].ContentType, all[0].State, all[0].LatestVersion));
        Assert.Equal(all.Select(hit => hit.EntryId), prefix.Select(hit => hit.EntryId));
        Assert.Equal([page], pagesOnly.Select(hit => hit.EntryId));
        Assert.Equal([product, page], twoWords.Select(hit => hit.EntryId));
        Assert.DoesNotContain(all, hit => hit.EntryId == other);
        Assert.Empty(publishedBefore);
        Assert.Equal([product], publishedAfter.Select(hit => hit.EntryId));
    }

    // In review is read from the change sets, as the entry tools read it, so a hit's state and the filter agree with get_entry.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task The_state_filter_keeps_entries_by_the_state_the_entry_tools_show_in_review_included(string provider)
    {
        await using var scenario = await EntryChangeSetTests.ScenarioAsync(provider);
        var (page, product) = await EntryChangeSetTests.CatalogAsync(scenario);
        await scenario.PublishAsync(EntryChangeSetTests.Operation(OperationKind.Create, ItemKind.Entity, product, null, 1));
        var review = await scenario.OpenAsync(EntryChangeSetTests.Operation(OperationKind.Create, ItemKind.Entry, page, null, 1));
        Assert.True((await scenario.RunAsync(directory => directory.TransitionAsync(review.Id, ChangeSetState.ChecksRunning))).IsSuccess);

        var all = await SearchAsync(scenario.Services, new SearchQuery("widget"));
        var published = await SearchAsync(scenario.Services, new SearchQuery("widget") { States = [EntryState.Published] });
        var inReview = await SearchAsync(scenario.Services, new SearchQuery("widget") { States = [EntryState.InReview] });
        var drafts = await SearchAsync(scenario.Services, new SearchQuery("widget") { States = [EntryState.Draft] });

        Assert.Equal(
            [(page, EntryState.InReview), (product, EntryState.Published)],
            all.Select(hit => (hit.EntryId, hit.State)).OrderBy(hit => hit.State == EntryState.Published));
        Assert.Equal([product], published.Select(hit => hit.EntryId));
        Assert.Equal([page], inReview.Select(hit => hit.EntryId));
        Assert.Empty(drafts);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_entry_is_found_once_its_write_commits_and_not_after_a_rollback_or_while_deleted(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(provider);
        await scenario.Schema.CreateAsync("note", Field("text", "text"));

        await using (var scope = scenario.Services.CreateAsyncScope())
        {
            await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync();
            Assert.True((await scope.ServiceProvider.GetRequiredService<IEntryDirectory>().CreateAsync(new NewEntry("note", Json("""{"text": "rolled back aurora"}""")))).IsSuccess);
        }

        var note = (await scenario.CreateAsync("note", """{"text": "first draft borealis"}""")).Value;
        var found = await SearchAsync(scenario, new SearchQuery("borealis"));
        var rolledBack = await SearchAsync(scenario, new SearchQuery("aurora"));

        await scenario.UpdateAsync(note.Id, 1, """{"text": "second draft australis"}""");
        var oldWord = await SearchAsync(scenario, new SearchQuery("borealis"));
        var newWord = await SearchAsync(scenario, new SearchQuery("australis"));

        Assert.True((await scenario.RunAsync(entries => entries.DeleteAsync(note.Id, 2))).IsSuccess);
        var deleted = await SearchAsync(scenario, new SearchQuery("australis"));
        await scenario.RunAsync(entries => entries.RestoreAsync(note.Id));
        var restored = await SearchAsync(scenario, new SearchQuery("australis"));

        Assert.Equal([note.Id], found.Select(hit => hit.EntryId));
        Assert.Empty(rolledBack);
        Assert.Empty(oldWord);
        Assert.Equal([note.Id], newWord.Select(hit => hit.EntryId));
        Assert.Empty(deleted);
        Assert.Equal([note.Id], restored.Select(hit => hit.EntryId));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Entries_that_existed_before_the_index_are_found_after_the_migration(string provider)
    {
        await using var scenario = await CatalogAsync(provider);

        await using (var scope = scenario.Entries.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>().Database;
            var migrations = database.GetMigrations().ToList();
            var index = migrations.FindIndex(name => name.EndsWith("_SearchDocuments", StringComparison.Ordinal));
            await database.GetService<IMigrator>().MigrateAsync(migrations[index - 1]);
            await database.MigrateAsync();
        }

        var hits = await SearchAsync(scenario.Entries, new SearchQuery("widget 4411"));

        Assert.Equal([scenario.Product, scenario.Page], hits.Select(hit => hit.EntryId));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_query_without_words_with_too_many_an_empty_filter_or_a_limit_out_of_range_is_refused(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(provider);

        var empty = await RunAsync(scenario, new SearchQuery(" -- "));
        var tooMany = await RunAsync(scenario, new SearchQuery(string.Join(' ', Enumerable.Range(1, SearchTerms.MaxTerms + 1).Select(word => $"w{word}"))));
        var limit = await RunAsync(scenario, new SearchQuery("widget") { Limit = 0 });
        var noTypes = await RunAsync(scenario, new SearchQuery("widget") { ContentTypes = [] });
        var noStates = await RunAsync(scenario, new SearchQuery("widget") { States = [] });

        Assert.Equal(SearchErrorCodes.QueryEmpty, Assert.Single(empty.Errors).Code);
        Assert.Equal(SearchErrorCodes.FilterEmpty, Assert.Single(noTypes.Errors).Code);
        Assert.Equal(SearchErrorCodes.FilterEmpty, Assert.Single(noStates.Errors).Code);
        Assert.Equal(SearchErrorCodes.QueryTooLong, Assert.Single(tooMany.Errors).Code);
        Assert.Equal(SearchErrorCodes.LimitInvalid, Assert.Single(limit.Errors).Code);
    }

    // A product entity with a SKU, a page that references it, and an unrelated page.
    private static async Task<Catalog> CatalogAsync(string provider)
    {
        var entries = await EntryScenario.CreateAsync(provider);
        await entries.Schema.CreateEntityAsync("product", Field("name", "text"), Field("sku", "text"));
        await entries.Schema.CreateAsync("page", Field("title", "text"), Reference("product", "product"));

        var product = (await entries.CreateAsync("product", $$"""{"name": {{Fact("\"Widget\"")}}, "sku": {{Fact("\"A-4411\"")}}}""")).Value.Id;
        var page = (await entries.CreateAsync("page", $$"""{"title": "Our best seller", "product": "{{product}}"}""")).Value.Id;
        var other = (await entries.CreateAsync("page", """{"title": "Opening hours", "product": null}""")).Value.Id;
        return new Catalog(entries, product, page, other);
    }

    private static Task<IReadOnlyList<SearchHit>> SearchAsync(EntryScenario scenario, SearchQuery query)
    {
        return SearchAsync(scenario.Services, query);
    }

    private static async Task<IReadOnlyList<SearchHit>> SearchAsync(IServiceProvider services, SearchQuery query)
    {
        var results = await RunAsync(services, query);
        Assert.True(results.IsSuccess, string.Join("; ", results.Errors.Select(error => error.Message)));
        return results.Value.Hits;
    }

    private static Task<Result<SearchResults>> RunAsync(EntryScenario scenario, SearchQuery query)
    {
        return RunAsync(scenario.Services, query);
    }

    private static async Task<Result<SearchResults>> RunAsync(IServiceProvider services, SearchQuery query)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISearch>().SearchAsync(query);
    }

    private sealed record Catalog(EntryScenario Entries, Guid Product, Guid Page, Guid Other) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            return Entries.DisposeAsync();
        }
    }
}
