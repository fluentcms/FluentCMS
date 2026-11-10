using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Mcp.Tests.McpTesting;
using static FluentCMS.Plugins.Mcp.Tests.McpToolsTesting;

namespace FluentCMS.Plugins.Mcp.Tests;

// The query tool and the list tool of a type build the query of the REST APIs, so a filter in JSON reads what the same
// filter in brackets reads on the Management API.
public sealed class McpQueryToolTests(McpToolsFixture fixture) : IClassFixture<McpToolsFixture>
{
    private const string Posts = "/api/management/v1/content/post";

    private static readonly string[] Opinion = ["opinion"];
    private static readonly string[] ByRatingThenTitle = ["-rating", "title"];
    private static readonly string[] ByTitle = ["title"];
    private static readonly string[] Shown = ["title", "rating", "category"];
    private static readonly int[] OneToFour = [1, 4];

    [Fact]
    public async Task AFilterInJsonReturnsTheEntriesOfTheManagementApiWithTheSameFilterInBrackets()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var marker = await SeedAsync(client);

        var fromTool = await OkAsync(client, McpContentTools.Query, new
        {
            type = "post",
            filter = new
            {
                note = new { eq = marker },
                rating = new { gte = 2 },
                or = new object[] { new { title = new { startsWith = "A" } }, new { category = new { @in = Opinion } } },
                not = new { title = new { eq = "Avoided, always" } },
            },
            sort = ByRatingThenTitle,
            fields = Shown,
            pageSize = 2,
        });
        var fromApi = await ApiAsync(http, $"?filter[note][eq]={marker}&filter[rating][gte]=2&filter[or][0][title][startsWith]=A&filter[or][1][category][in]=opinion"
            + "&filter[not][title][eq]=Avoided%2C%20always&sort=-rating,title&fields=title,rating,category&pageSize=2");

        fromTool.GetProperty("items").EnumerateArray().Select(entry => entry.GetProperty("title").GetString()).ShouldBe(["Apples", "Opinions"]);
        fromTool.GetProperty("total").GetInt32().ShouldBe(2);
        Same(fromTool, fromApi);
    }

    [Fact]
    public async Task TheListToolOfATypeReadsWhatTheManagementListReads()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var marker = await SeedAsync(client);

        var firstPage = await OkAsync(client, "post_list", new { filter = new { note = new { eq = marker }, rating = new { between = OneToFour } }, sort = ByTitle, pageSize = 2, withTotal = false });
        var secondPage = await OkAsync(client, "post_list", new { filter = new { note = new { eq = marker }, rating = new { between = OneToFour } }, sort = ByTitle, pageSize = 2, cursor = firstPage.GetProperty("nextCursor").GetString() });
        var published = await OkAsync(client, "post_list", new { filter = new { note = new { eq = marker } }, status = "published" });

        Same(firstPage, await ApiAsync(http, $"?filter[note][eq]={marker}&filter[rating][between]=1,4&sort=title&pageSize=2&withTotal=false"));
        firstPage.TryGetProperty("total", out _).ShouldBeFalse();
        firstPage.GetProperty("items").EnumerateArray().Select(entry => entry.GetProperty("title").GetString()).ShouldBe(["Asides", "Avoided, always"]);
        secondPage.GetProperty("items").EnumerateArray().Select(entry => entry.GetProperty("title").GetString()).ShouldBe(["Bananas", "Opinions"]);
        published.GetProperty("items").EnumerateArray().Select(entry => entry.GetProperty("title").GetString()).ShouldBe(["Apples"]);
    }

    [Fact]
    public async Task AQueryTheGrammarRefusesIsRefusedWithTheCodeOfTheRestApis()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var operation = await CallAsync(client, McpContentTools.Query, new { type = "post", filter = new { title = new { like = "A" } } });
        var field = await CallAsync(client, "post_list", new { filter = new { colour = new { eq = "red" } } });
        var site = await CallAsync(client, McpContentTools.Query, new { type = "post", site = Guid.NewGuid() });
        var search = await CallAsync(client, McpContentTools.Query, new { type = "post", q = "apples" });

        Code(operation).ShouldBe("FCMS2203");
        Text(operation).ShouldContain("filter[title][like]");
        Code(field).ShouldBe("FCMS2202");
        Code(site).ShouldBe("FCMS2108");
        Text(site).ShouldContain("- site: ");
        Code(search).ShouldBe("FCMS2211");
    }

    // Five posts that only this test reads, by a note of its own, of which one is published, and one of another note.
    private static async Task<string> SeedAsync(McpClient client)
    {
        var marker = "m" + Guid.NewGuid().ToString("N");
        var apples = await OkAsync(client, "post_create_draft", new { fields = new { title = "Apples", rating = 5, category = "news", note = marker } });
        await OkAsync(client, "post_create_draft", new { fields = new { title = "Avoided, always", rating = 4, category = "news", note = marker } });
        await OkAsync(client, "post_create_draft", new { fields = new { title = "Bananas", rating = 3, category = "news", note = marker } });
        await OkAsync(client, "post_create_draft", new { fields = new { title = "Opinions", rating = 2, category = "opinion", note = marker } });
        await OkAsync(client, "post_create_draft", new { fields = new { title = "Asides", rating = 1, category = "opinion", note = marker } });
        await OkAsync(client, "post_create_draft", new { fields = new { title = "Almonds", rating = 5, category = "news", note = "another " + marker } });
        await OkAsync(client, McpContentTools.Publish, new { type = "post", id = apples.GetProperty("id").GetGuid() });
        return marker;
    }

    private static async Task<JsonNode> ApiAsync(HttpClient http, string query) =>
        (await http.GetFromJsonAsync<JsonNode>(new Uri(Posts + query, UriKind.Relative), Token))!;

    private static void Same(JsonElement fromTool, JsonNode fromApi) =>
        JsonNode.DeepEquals(JsonNode.Parse(fromTool.GetRawText()), fromApi).ShouldBeTrue($"{fromTool.GetRawText()}\n{fromApi.ToJsonString()}");
}
