using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Mcp.Tests.McpTesting;
using static FluentCMS.Plugins.Mcp.Tests.McpToolsTesting;

namespace FluentCMS.Plugins.Mcp.Tests;

// The sizes of what goes in and comes out: a request over its limit is not read, a list over its limit ends at an entry
// with the cursor of the rest, and a long text is shortened in a list.
public sealed class McpSizeLimitTests
{
    private const int MaxResultBytes = 4096;
    private const int Essays = 30;

    private static readonly string[] ByTitle = ["title"];

    [Fact]
    public async Task AListOverTheResultSizeEndsAtAnEntryBoundaryWithTruncatedAndACursorAndTheNextCallContinuesFromIt()
    {
        await using var host = Host();
        using var http = await KeyAsync(host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var titles = await SeedAsync(client);

        var first = await OkAsync(client, "essay_list", new { sort = ByTitle, pageSize = Essays });
        List<JsonElement> answers = [first];
        while (answers[^1].TryGetProperty("nextCursor", out var cursor))
        {
            answers.Add(await OkAsync(client, "essay_list", new { sort = ByTitle, pageSize = Essays, cursor = cursor.GetString() }));
        }

        first.GetProperty("truncated").GetBoolean().ShouldBeTrue();
        first.GetProperty("total").GetInt32().ShouldBe(Essays);
        first.GetProperty("pageSize").GetInt32().ShouldBe(first.GetProperty("items").GetArrayLength());
        first.GetProperty("items").GetArrayLength().ShouldBeInRange(2, Essays - 1);
        answers.Count.ShouldBeGreaterThan(2);
        answers.ShouldAllBe(answer => Encoding.UTF8.GetByteCount(answer.GetRawText()) <= MaxResultBytes);

        // Every entry is whole, and the answers together are the list: none twice and none left out.
        var entries = answers.SelectMany(answer => answer.GetProperty("items").EnumerateArray()).ToList();
        entries.ShouldAllBe(entry => entry.GetProperty("id").GetGuid() != Guid.Empty && entry.GetProperty("body").GetString() != null && entry.GetProperty("version").GetInt32() == 1);
        entries.Select(entry => entry.GetProperty("title").GetString()).ShouldBe(titles);
        answers[^1].TryGetProperty("truncated", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task AListReadByPageOrByQueryIsCutTheSameWay()
    {
        await using var host = Host();
        using var http = await KeyAsync(host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var titles = await SeedAsync(client);

        var second = await OkAsync(client, McpContentTools.Query, new { type = "essay", sort = ByTitle, page = 2, pageSize = 15, withTotal = false });
        List<JsonElement> answers = [second];
        while (answers[^1].TryGetProperty("nextCursor", out var cursor))
        {
            answers.Add(await OkAsync(client, McpContentTools.Query, new { type = "essay", sort = ByTitle, pageSize = 15, cursor = cursor.GetString() }));
        }

        var small = await OkAsync(client, "essay_list", new { sort = ByTitle, page = 1, pageSize = 3 });

        second.GetProperty("truncated").GetBoolean().ShouldBeTrue();
        second.TryGetProperty("total", out _).ShouldBeFalse();
        answers.SelectMany(answer => answer.GetProperty("items").EnumerateArray()).Select(entry => entry.GetProperty("title").GetString()).ShouldBe(titles.Skip(15));

        // A list within the size is answered as it was.
        small.TryGetProperty("truncated", out _).ShouldBeFalse();
        small.GetProperty("page").GetInt32().ShouldBe(1);
        small.GetProperty("pageSize").GetInt32().ShouldBe(3);
        small.GetProperty("items").GetArrayLength().ShouldBe(3);
    }

    [Fact]
    public async Task ALongTextIsShortenedInAListWithAMarkerAndWholeInGet()
    {
        await using var host = Host();
        using var http = await KeyAsync(host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var body = string.Concat(Enumerable.Repeat("All the words of a long essay. ", 20));
        var id = (await OkAsync(client, "essay_create_draft", new { fields = new { title = "Long", body } })).GetProperty("id").GetGuid();

        var listed = (await OkAsync(client, "essay_list")).GetProperty("items")[0];
        var queried = (await OkAsync(client, McpContentTools.Query, new { type = "essay" })).GetProperty("items")[0];
        var whole = await OkAsync(client, "essay_get", new { id });

        var shortened = listed.GetProperty("body").GetString().ShouldNotBeNull();
        shortened.ShouldStartWith(body[..40]);
        shortened.ShouldEndWith($"… [shortened: {body.Length} characters in all; essay_get reads it whole]");
        shortened.Length.ShouldBeLessThan(body.Length);
        listed.GetProperty("title").GetString().ShouldBe("Long");
        queried.GetProperty("body").GetString().ShouldBe(shortened);
        whole.GetProperty("body").GetString().ShouldBe(body);
    }

    // A type over the cap of exposed types has no {type}_get, so the query tool is where its text is read whole.
    [Fact]
    public async Task ATextOfATypeWithoutToolsOfItsOwnStaysWholeInAQuery()
    {
        await using var host = McpSafetyTesting.Host(options =>
        {
            options.MaxListTextLength = 40;
            options.MaxExposedTypes = 0;
        });
        using var http = await KeyAsync(host);
        var body = new string('x', 200);
        using var created = await http.PostAsJsonAsync(new Uri("/api/management/v1/content/essay", UriKind.Relative), new { title = "Long", body }, Token);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var queried = (await OkAsync(client, McpContentTools.Query, new { type = "essay" })).GetProperty("items")[0];

        queried.GetProperty("body").GetString().ShouldBe(body);
    }

    [Fact]
    public async Task ARequestOverTheSizeLimitIsRefusedWithItsCode()
    {
        await using var host = McpSafetyTesting.Host(options => options.MaxRequestBytes = 4096);
        using var http = await KeyAsync(host);

        using var small = await SendAsync(http, new string('x', 1000));
        using var large = await SendAsync(http, new string('x', 5000));

        small.StatusCode.ShouldBe(HttpStatusCode.OK);
        large.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await CodeAsync(large)).ShouldBe("FCMS3111");
        (await JsonAsync(large))["detail"]!.GetValue<string>().ShouldContain("4096");
    }

    private static FluentCMS.Testing.FluentCmsTestHost Host() => McpSafetyTesting.Host(options =>
    {
        options.MaxResultBytes = MaxResultBytes;
        options.MaxListTextLength = 40;
        options.Limits = new McpCallLimitOptions { CallsPerMinute = 1000, WritesPerMinute = 1000 };
    });

    // Essays whose titles sort in the order they are made, each with a body that a list shortens.
    private static async Task<List<string>> SeedAsync(McpClient client)
    {
        List<string> titles = [];
        for (var number = 0; number < Essays; number++)
        {
            var title = $"Essay {number:D2}";
            await OkAsync(client, "essay_create_draft", new { fields = new { title, body = new string((char)('a' + (number % 26)), 300) } });
            titles.Add(title);
        }

        return titles;
    }

    // A ping that carries text it does not need, so that the body has the size a test asks for.
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string padding)
    {
        var body = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "ping", ["params"] = new JsonObject { ["_meta"] = new JsonObject { ["padding"] = padding } } };
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/mcp", UriKind.Relative))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return await client.SendAsync(request, Token);
    }
}
