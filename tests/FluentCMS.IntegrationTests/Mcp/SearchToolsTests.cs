using System.Text.Json;
using FluentCMS.Core.Content;
using FluentCMS.Core.Search;
using FluentCMS.Core.Tools;
using FluentCMS.Infrastructure.Persistence;

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

        var hit = Assert.Single(found["hits"]!.AsArray())!;
        Assert.Equal((page.ToString(), "page", "draft", (string?)null), ((string?)hit["id"], (string?)hit["contentType"], (string?)hit["state"], (string?)hit["via"]));
        Assert.Single(drafts["hits"]!.AsArray());
    }

    [Fact]
    public async Task A_query_without_words_or_an_unknown_state_is_a_tool_error()
    {
        await using var agent = await ConnectedAgent.StartAsync(DatabaseProviders.Sqlite);

        var empty = await agent.FailAsync("search_entries", new() { ["query"] = "?!" });
        var state = await agent.FailAsync("search_entries", new() { ["query"] = "4411", ["states"] = new[] { "live" } });

        Assert.Equal(SearchErrorCodes.QueryEmpty, (string?)empty["errors"]![0]!["code"]);
        Assert.Equal(ToolErrorCodes.ArgumentInvalid, (string?)state["errors"]![0]!["code"]);
        Assert.Contains("'live' is not an entry state", (string?)state["errors"]![0]!["message"]);
    }

    private static async Task<Guid> CreateAsync(ConnectedAgent agent, string type, string title)
    {
        var fields = JsonSerializer.SerializeToElement(new { title });
        return (await agent.AsPersonAsync<IEntryDirectory, Entry>(entries => entries.CreateAsync(new NewEntry(type, fields)))).Id;
    }
}
