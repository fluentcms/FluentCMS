using System.Text.Json;
using FluentCMS.Core.Agents;
using FluentCMS.Core.Content;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Search;
using FluentCMS.Core.Tools;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Mcp;

public sealed class SearchToolsTests
{
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_finds_entries_of_the_types_it_may_read_and_no_others(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider, types: ["page"]);
        await agent.CreateAsPersonAsync("page");
        await agent.CreateAsPersonAsync("product");
        var page = await CreateAsync(agent, "page", "Spring sale on part 4411");
        await CreateAsync(agent, "product", "Part 4411");

        var found = await agent.CallAsync("search_entries", new() { ["query"] = "4411" });
        var drafts = await agent.CallAsync("search_entries", new() { ["query"] = "4411", ["states"] = new[] { "draft" }, ["limit"] = 5 });
        var published = await agent.CallAsync("search_entries", new() { ["query"] = "4411", ["states"] = new[] { "published" } });
        var service = await agent.AsAgentAsync(services => services.GetRequiredService<ISearch>().SearchAsync(new SearchQuery("4411")));

        var hit = Assert.Single(found["hits"]!.AsArray())!;
        Assert.Equal((page.ToString(), "page", "draft", (string?)null), ((string?)hit["id"], (string?)hit["contentType"], (string?)hit["state"], (string?)hit["via"]));
        Assert.Equal([page], service.Value.Hits.Select(serviceHit => serviceHit.EntryId));
        Assert.Single(drafts["hits"]!.AsArray());
        Assert.Empty(published["hits"]!.AsArray());
        Assert.True((bool)found["complete"]!);
        Assert.Contains("get_entry", (string?)found["next"]);
        Assert.Contains("Nothing matched", (string?)published["next"]);
    }

    // Matches the agent may not read are dropped before the limit, so better-ranked ones of other types do not crowd out its own.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Matches_outside_the_scope_do_not_crowd_out_the_ones_the_agent_may_read(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider, types: ["page"]);
        await agent.CreateAsPersonAsync("page");
        await agent.CreateAsPersonAsync("product");
        foreach (var _ in Enumerable.Range(0, 15))
        {
            await CreateAsync(agent, "product", "4411 4411 4411");
        }

        var page = await CreateAsync(agent, "page", "Spring sale on part 4411, while the stock lasts, in every store");

        var found = await agent.CallAsync("search_entries", new() { ["query"] = "4411", ["limit"] = 1 });

        Assert.Equal(page.ToString(), (string?)Assert.Single(found["hits"]!.AsArray())!["id"]);
    }

    // Only matches the agent may read lead to the entries that reference them, so a page found through a product would show the
    // product's id to an agent that may not read products.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_page_is_not_found_through_a_match_the_agent_may_not_read(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider, types: ["page"]);
        await agent.AsPersonAsync<IContentTypeDirectory, ContentType>(directory =>
            directory.CreateAsync(new NewContentType("product", "product", new ContentTypeDefinition([new FieldDefinition("title", "text")])), TestChangeSetTargets.ContentModel));
        await agent.AsPersonAsync<IContentTypeDirectory, ContentType>(directory =>
            directory.CreateAsync(new NewContentType("page", "page", new ContentTypeDefinition(
                [new FieldDefinition("title", "text"), new FieldDefinition("product", "reference", settings: JsonSerializer.SerializeToElement(new { target = "product" }))])), TestChangeSetTargets.ContentModel));
        var product = await CreateAsync(agent, "product", "Part 4411");
        var page = (await agent.AsPersonAsync<IEntryDirectory, Entry>(entries =>
            entries.CreateAsync(new NewEntry("page", JsonSerializer.SerializeToElement(new { title = "Spring sale", product = product.ToString() }))))).Id;

        var found = await agent.CallAsync("search_entries", new() { ["query"] = "4411" });
        var person = await agent.AsPersonAsync<ISearch, SearchResults>(search => search.SearchAsync(new SearchQuery("4411")));

        Assert.Empty(found["hits"]!.AsArray());
        Assert.Equal([(product, (Guid?)null), (page, product)], person.Hits.Select(hit => (hit.EntryId, hit.Via)));
    }

    // Products are entities, so their price is a fact and the filter compares the fact's value.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_filters_by_field_values_and_references_with_or_without_words(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        await agent.AsPersonAsync<IContentTypeDirectory, ContentType>(directory =>
            directory.CreateAsync(new NewContentType("product", "product", new ContentTypeDefinition(
                [new FieldDefinition("title", "text"), new FieldDefinition("price", "number")],
                isEntity: true))));
        await agent.AsPersonAsync<IContentTypeDirectory, ContentType>(directory =>
            directory.CreateAsync(new NewContentType("page", "page", new ContentTypeDefinition(
                [new FieldDefinition("title", "text"), new FieldDefinition("product", "reference", settings: JsonSerializer.SerializeToElement(new { target = "product" }))]))));
        var cheap = await CreateAsync(agent, "product", new { title = Fact("Part 4411"), price = Fact(19.99) });
        var dear = await CreateAsync(agent, "product", new { title = Fact("Part 4412"), price = Fact(25) });
        var page = await CreateAsync(agent, "page", new { title = "Spring sale", product = cheap.ToString() });
        var earlier = await CreateAsync(agent, "page", new { title = "Last season", product = cheap.ToString() });
        await agent.AsPersonAsync<IEntryDirectory, Entry>(entries =>
            entries.UpdateAsync(earlier, 1, JsonSerializer.SerializeToElement(new { title = "Last season", product = (string?)null })));

        var underTwenty = await agent.CallAsync("search_entries", new()
        {
            ["query"] = "part",
            ["types"] = new[] { "product" },
            ["fields"] = new[] { new { field = "price", @operator = "less_than", value = 20 } },
        });
        var noWords = await agent.CallAsync("search_entries", new()
        {
            ["types"] = new[] { "product" },
            ["fields"] = new[] { new { field = "title", @operator = "contains", value = "PART" } },
        });
        var referencing = await agent.CallAsync("search_entries", new() { ["types"] = new[] { "page" }, ["references"] = cheap.ToString() });
        var refused = await agent.FailAsync("search_entries", new()
        {
            ["types"] = new[] { "product" },
            ["fields"] = new object[] { new { field = "price", @operator = "below", value = 20 }, new { field = "price" } },
        });

        Assert.Equal([cheap.ToString()], underTwenty["hits"]!.AsArray().Select(hit => (string?)hit!["id"]));
        Assert.Equal(new[] { cheap, dear }.Order().Select(id => id.ToString()), noWords["hits"]!.AsArray().Select(hit => (string?)hit!["id"]));
        Assert.Equal([page.ToString()], referencing["hits"]!.AsArray().Select(hit => (string?)hit!["id"]));
        Assert.Equal([ToolErrorCodes.ArgumentInvalid, ToolErrorCodes.ArgumentInvalid], refused["errors"]!.AsArray().Select(error => (string?)error!["code"]));
        Assert.Contains("'below' is not an operator", (string?)refused["errors"]![0]!["message"]);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Field_filters_that_do_not_fit_one_type_are_tool_errors_with_their_codes(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        await agent.AsPersonAsync<IContentTypeDirectory, ContentType>(directory =>
            directory.CreateAsync(new NewContentType("product", "product", new ContentTypeDefinition(
                [new FieldDefinition("title", "text"), new FieldDefinition("price", "number")]))));
        await agent.CreateAsPersonAsync("page");

        async Task<string?> CodeAsync(Dictionary<string, object?> arguments)
        {
            return (string?)(await agent.FailAsync("search_entries", arguments))["errors"]![0]!["code"];
        }

        var byPrice = new[] { new { field = "price", @operator = "less_than", value = 20 } };
        Assert.Equal(SearchErrorCodes.FilterNeedsOneType, await CodeAsync(new() { ["query"] = "part", ["fields"] = byPrice }));
        Assert.Equal(SearchErrorCodes.FilterNeedsOneType, await CodeAsync(new() { ["query"] = "part", ["types"] = new[] { "product", "page" }, ["fields"] = byPrice }));
        Assert.Equal(SearchErrorCodes.FieldNotFound, await CodeAsync(new()
        {
            ["types"] = new[] { "product" },
            ["fields"] = new[] { new { field = "colour", @operator = "equals", value = "red" } },
        }));
        Assert.Equal(SearchErrorCodes.OperatorNotSupported, await CodeAsync(new()
        {
            ["types"] = new[] { "product" },
            ["fields"] = new[] { new { field = "price", @operator = "contains", value = "19" } },
        }));
        Assert.Equal(SearchErrorCodes.FilterValueInvalid, await CodeAsync(new()
        {
            ["types"] = new[] { "product" },
            ["fields"] = new[] { new { field = "price", @operator = "less_than", value = "twenty" } },
        }));
    }

    private static object Fact(object value)
    {
        return new { value, source = (string?)null, owner = (string?)null, validFrom = (string?)null, validUntil = (string?)null, lastVerified = (string?)null };
    }

    [Fact]
    public async Task A_type_filter_naming_an_unknown_type_or_one_outside_the_scope_is_a_tool_error()
    {
        await using var agent = await ConnectedAgent.StartAsync(DatabaseProviders.Sqlite, types: ["page"]);
        await agent.CreateAsPersonAsync("page");
        await agent.CreateAsPersonAsync("product");

        var refused = await agent.FailAsync("search_entries", new() { ["query"] = "4411", ["types"] = new[] { "page", "prodcut", "product" } });

        Assert.Equal(
            [SchemaErrorCodes.ContentTypeNotFound, AgentPermissionErrorCodes.ScopeDenied],
            refused["errors"]!.AsArray().Select(error => (string?)error!["code"]));
    }

    [Fact]
    public async Task A_query_without_words_an_unknown_state_or_an_empty_filter_is_a_tool_error()
    {
        await using var agent = await ConnectedAgent.StartAsync(DatabaseProviders.Sqlite);

        var empty = await agent.FailAsync("search_entries", new() { ["query"] = "?!" });
        var state = await agent.FailAsync("search_entries", new() { ["query"] = "4411", ["states"] = new[] { "live" } });
        var noTypes = await agent.FailAsync("search_entries", new() { ["query"] = "4411", ["types"] = Array.Empty<string>() });
        var noStates = await agent.FailAsync("search_entries", new() { ["query"] = "4411", ["states"] = Array.Empty<string>() });

        Assert.Equal(SearchErrorCodes.QueryEmpty, (string?)empty["errors"]![0]!["code"]);
        Assert.Equal(SearchErrorCodes.FilterEmpty, (string?)noTypes["errors"]![0]!["code"]);
        Assert.Contains("types filter is empty", (string?)noTypes["errors"]![0]!["message"]);
        Assert.Equal(SearchErrorCodes.FilterEmpty, (string?)noStates["errors"]![0]!["code"]);
        Assert.Equal(ToolErrorCodes.ArgumentInvalid, (string?)state["errors"]![0]!["code"]);
        Assert.Contains("'live' is not an entry state", (string?)state["errors"]![0]!["message"]);
    }

    private static Task<Guid> CreateAsync(ConnectedAgent agent, string type, string title)
    {
        return CreateAsync(agent, type, new { title });
    }

    private static async Task<Guid> CreateAsync(ConnectedAgent agent, string type, object fields)
    {
        var json = JsonSerializer.SerializeToElement(fields);
        return (await agent.AsPersonAsync<IEntryDirectory, Entry>(entries => entries.CreateAsync(new NewEntry(type, json)))).Id;
    }
}
