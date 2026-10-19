using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Search;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.Infrastructure.Search;
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
        var productId = await SearchAsync(scenario.Entries, new SearchQuery(product.ToString()[..8]));

        Assert.Equal([(product, (Guid?)null), (page, product)], all.Select(hit => (hit.EntryId, hit.Via)));
        Assert.Equal(("product", EntryState.Draft, 1), (all[0].ContentType, all[0].State, all[0].LatestVersion));
        Assert.Equal(all.Select(hit => hit.EntryId), prefix.Select(hit => hit.EntryId));
        Assert.Equal([page], pagesOnly.Select(hit => hit.EntryId));
        Assert.Equal([product, page], twoWords.Select(hit => hit.EntryId));
        Assert.DoesNotContain(all, hit => hit.EntryId == other);
        Assert.Empty(publishedBefore);
        Assert.Equal([product], publishedAfter.Select(hit => hit.EntryId));
        Assert.Empty(productId);
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

    // The index row is written in the write's transaction, so an update that rolls back leaves the old words found and the new ones not.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_update_that_rolls_back_leaves_the_index_as_it_was(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(provider);
        await scenario.Schema.CreateAsync("note", Field("text", "text"));
        var note = (await scenario.CreateAsync("note", """{"text": "first draft borealis"}""")).Value;

        await using (var scope = scenario.Services.CreateAsyncScope())
        {
            await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync();
            var updated = await scope.ServiceProvider.GetRequiredService<IEntryDirectory>().UpdateAsync(note.Id, 1, Json("""{"text": "second draft australis"}"""));
            Assert.True(updated.IsSuccess);
        }

        Assert.Equal([note.Id], (await SearchAsync(scenario, new SearchQuery("borealis"))).Select(hit => hit.EntryId));
        Assert.Empty(await SearchAsync(scenario, new SearchQuery("australis")));
    }

    // Better-ranked matches a filter drops are read past a page at a time, up to the most a search reads, and a search that stops
    // there says so.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_filtered_search_reads_past_better_ranked_matches_and_says_when_it_stopped_early(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(
            provider,
            services => services.Configure<SearchOptions>(search => (search.PageSize, search.MaxMatches) = (20, 60)));
        await scenario.Schema.CreateAsync("note", Field("text", "text"));
        await scenario.Schema.CreateAsync("page", Field("title", "text"));
        foreach (var _ in Enumerable.Range(0, 45))
        {
            await scenario.CreateAsync("note", """{"text": "zephyr zephyr zephyr"}""");
        }

        var page = (await scenario.CreateAsync("page", """{"title": "A zephyr in a much longer title of the page with many other words"}""")).Value;
        var found = await RunAsync(scenario, new SearchQuery("zephyr") { ContentTypes = ["page"] });

        foreach (var _ in Enumerable.Range(0, 20))
        {
            await scenario.CreateAsync("note", """{"text": "zephyr zephyr zephyr"}""");
        }

        var stopped = await RunAsync(scenario, new SearchQuery("zephyr") { ContentTypes = ["page"] });
        var unfiltered = await RunAsync(scenario, new SearchQuery("zephyr") { Limit = 5 });

        Assert.Equal([page.Id], found.Value.Hits.Select(hit => hit.EntryId));
        Assert.True(found.Value.Complete);
        Assert.Equal((0, false), (stopped.Value.Hits.Count, stopped.Value.Complete));
        Assert.Equal((5, true), (unfiltered.Value.Hits.Count, unfiltered.Value.Complete));
    }

    // An entity's price is a fact, so the filter compares the fact's value; text and filters both have to hold.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Field_filters_narrow_a_text_search_and_filter_one_type_without_words(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(provider);
        await scenario.Schema.CreateEntityAsync("product", Field("name", "text"), Field("price", "number"));
        var cheap = (await scenario.CreateAsync("product", $$"""{"name": {{Fact("\"Widget\"")}}, "price": {{Fact("19.99")}}}""")).Value.Id;
        await scenario.CreateAsync("product", $$"""{"name": {{Fact("\"Widget Pro\"")}}, "price": {{Fact("25")}}}""");
        await scenario.CreateAsync("product", $$"""{"name": {{Fact("\"Gadget\"")}}, "price": {{Fact("5")}}}""");

        var underTwenty = await SearchAsync(scenario, new SearchQuery("widget")
        {
            ContentTypes = ["product"],
            Fields = [new FieldFilter("price", FieldOperator.LessThan, Json("20"))],
        });
        var noWords = await SearchAsync(scenario, new SearchQuery(string.Empty)
        {
            ContentTypes = ["product"],
            Fields = [new FieldFilter("name", FieldOperator.Contains, Json("\"wid\""))],
        });

        Assert.Equal([cheap], underTwenty.Select(hit => hit.EntryId));
        Assert.Equal(2, noWords.Count);
        Assert.All(noWords, hit => Assert.Equal(0, hit.Rank));
    }

    // Only the latest version counts, so a page that referenced the product before an update is left out.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_reference_filter_keeps_the_entries_whose_latest_version_references_the_id(string provider)
    {
        await using var scenario = await CatalogAsync(provider);
        var earlier = (await scenario.Entries.CreateAsync("page", $$"""{"title": "Last season", "product": "{{scenario.Product}}"}""")).Value.Id;
        await scenario.Entries.UpdateAsync(earlier, 1, """{"title": "Last season", "product": null}""");

        var referencing = await SearchAsync(scenario.Entries, new SearchQuery(string.Empty) { ContentTypes = ["page"], References = scenario.Product });
        var withWords = await SearchAsync(scenario.Entries, new SearchQuery("seller") { References = scenario.Product });
        var otherWords = await SearchAsync(scenario.Entries, new SearchQuery("hours") { References = scenario.Product });

        Assert.Equal([scenario.Page], referencing.Select(hit => hit.EntryId));
        Assert.Equal([scenario.Page], withWords.Select(hit => hit.EntryId));
        Assert.Empty(otherWords);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Field_filters_that_name_no_single_type_or_do_not_fit_their_field_are_refused(string provider)
    {
        await using var scenario = await CatalogAsync(provider);
        var byName = new FieldFilter("name", FieldOperator.Contains, Json("\"wid\""));

        var noType = await RunAsync(scenario.Entries, new SearchQuery("widget") { Fields = [byName] });
        var twoTypes = await RunAsync(scenario.Entries, new SearchQuery("widget") { ContentTypes = ["product", "page"], Fields = [byName] });
        var noWordsNoType = await RunAsync(scenario.Entries, new SearchQuery(string.Empty) { References = scenario.Product });
        var emptyFields = await RunAsync(scenario.Entries, new SearchQuery("widget") { ContentTypes = ["product"], Fields = [] });
        var unknownField = await RunAsync(scenario.Entries, new SearchQuery("widget")
        {
            ContentTypes = ["product"],
            Fields = [new FieldFilter("colour", FieldOperator.EqualTo, Json("\"red\""))],
        });

        Assert.Equal(SearchErrorCodes.FilterNeedsOneType, Assert.Single(noType.Errors).Code);
        Assert.Equal(SearchErrorCodes.FilterNeedsOneType, Assert.Single(twoTypes.Errors).Code);
        Assert.Equal(SearchErrorCodes.FilterNeedsOneType, Assert.Single(noWordsNoType.Errors).Code);
        Assert.Equal(SearchErrorCodes.FilterEmpty, Assert.Single(emptyFields.Errors).Code);
        Assert.Equal(SearchErrorCodes.FieldNotFound, Assert.Single(unknownField.Errors).Code);
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
        var productId = await SearchAsync(scenario.Entries, new SearchQuery(scenario.Product.ToString()[..8]));

        Assert.Equal([scenario.Product, scenario.Page], hits.Select(hit => hit.EntryId));
        Assert.Empty(productId);
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
