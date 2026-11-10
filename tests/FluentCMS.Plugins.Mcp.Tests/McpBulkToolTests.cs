using System.Text.Json;
using ModelContextProtocol.Client;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Mcp.Tests.McpTesting;
using static FluentCMS.Plugins.Mcp.Tests.McpToolsTesting;

namespace FluentCMS.Plugins.Mcp.Tests;

// The bulk tool: one action on the entries of a type, by ids or by a filter, behind a dry run and under a limit.
public sealed class McpBulkToolTests(McpToolsFixture fixture) : IClassFixture<McpToolsFixture>
{
    [Fact]
    public async Task BulkNeedsALimitAndRefusesOneOverTheCapAndAFilterThatMatchesMoreThanTheLimit()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var (marker, ids) = await SeedAsync(client, "One", "Two", "Three");
        var filter = new { note = new { eq = marker } };

        var noLimit = await CallAsync(client, McpContentTools.Bulk, new { type = "post", action = "delete", filter });
        var overTheCap = await CallAsync(client, McpContentTools.Bulk, new { type = "post", action = "delete", filter, limit = 101 });
        var tooMany = await CallAsync(client, McpContentTools.Bulk, new { type = "post", action = "delete", filter, limit = 2 });
        var tooManyIds = await CallAsync(client, McpContentTools.Bulk, new { type = "post", action = "delete", ids, limit = 2 });
        var tooManyAtTheRun = await CallAsync(client, McpContentTools.Bulk, new { type = "post", action = "delete", filter, limit = 2, dryRun = false, confirmationToken = "v1.none" });
        var withinTheLimit = await OkAsync(client, McpContentTools.Bulk, new { type = "post", action = "delete", filter, limit = 3 });

        Code(noLimit).ShouldBe("FCMS2101");
        Text(noLimit).ShouldContain("- limit: ");
        Code(overTheCap).ShouldBe("FCMS1635");
        Text(overTheCap).ShouldContain("100");
        Code(tooMany).ShouldBe("FCMS1635");
        Code(tooManyIds).ShouldBe("FCMS1635");
        Code(tooManyAtTheRun).ShouldBe("FCMS1635");
        new[] { noLimit, overTheCap, tooMany, tooManyIds, tooManyAtTheRun }.ShouldAllBe(result => result.IsError == true);
        withinTheLimit.GetProperty("count").GetInt32().ShouldBe(3);
        (await ListAsync(client, marker)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task ABulkCallAnswersThePlanFirstAndThenTheOutcomeOfEachEntry()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var (marker, ids) = await SeedAsync(client, "Ready", "Also ready", "");
        var arguments = new { type = "post", action = "publish", filter = new { note = new { eq = marker } }, limit = 10 };

        var plan = await OkAsync(client, McpContentTools.Bulk, arguments);
        var untouched = await ListAsync(client, marker);
        var ran = await OkAsync(client, McpContentTools.Bulk, Confirmed(arguments, TokenOf(plan)));
        var after = await ListAsync(client, marker);

        plan.GetProperty("dryRun").GetBoolean().ShouldBeTrue();
        plan.GetProperty("action").GetString().ShouldBe("publish");
        plan.GetProperty("count").GetInt32().ShouldBe(3);
        plan.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("id").GetGuid()).ShouldBe(ids, ignoreOrder: true);
        plan.GetProperty("entries").EnumerateArray().ShouldAllBe(entry => entry.GetProperty("status").GetString() == "draft" && entry.GetProperty("version").GetInt32() == 1);
        plan.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("title").GetString()).ShouldBe(["Ready", "Also ready", ""], ignoreOrder: true);
        untouched.ShouldAllBe(entry => entry.GetProperty("status").GetString() == "draft");

        // As the bulk route of the Management API answers: one item for each entry, with its outcome.
        ran.GetProperty("cancelled").GetBoolean().ShouldBeFalse();
        var items = ran.GetProperty("items").EnumerateArray().ToDictionary(item => item.GetProperty("id").GetGuid());
        items.Keys.ShouldBe(ids, ignoreOrder: true);
        items[ids[0]].GetProperty("status").GetString().ShouldBe("succeeded");
        items[ids[1]].GetProperty("status").GetString().ShouldBe("succeeded");
        items[ids[2]].GetProperty("status").GetString().ShouldBe("failed");
        items[ids[2]].GetProperty("problem").GetProperty("code").GetString().ShouldNotBeNull().ShouldStartWith("FCMS21");
        ran.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("index").GetInt32()).ShouldBe([0, 1, 2]);
        after.Count(entry => entry.GetProperty("status").GetString() == "published").ShouldBe(2);
    }

    [Fact]
    public async Task ABulkCallByIdsActsOnTheEntriesItFindsAndNamesTheOthers()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var (marker, ids) = await SeedAsync(client, "First", "Second", "Kept");
        var unknown = Guid.NewGuid();
        var arguments = new { type = "post", action = "delete", ids = new[] { ids[0], ids[1], unknown }, limit = 3 };

        var plan = await OkAsync(client, McpContentTools.Bulk, arguments);
        var ran = await OkAsync(client, McpContentTools.Bulk, Confirmed(arguments, TokenOf(plan)));
        var replayed = await CallAsync(client, McpContentTools.Bulk, Confirmed(arguments, TokenOf(plan)));

        plan.GetProperty("count").GetInt32().ShouldBe(2);
        plan.GetProperty("notFound").EnumerateArray().Select(id => id.GetGuid()).ShouldBe([unknown]);
        ran.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ShouldBe([ids[0], ids[1]], ignoreOrder: true);
        ran.GetProperty("items").EnumerateArray().ShouldAllBe(item => item.GetProperty("status").GetString() == "succeeded");
        ran.GetProperty("notFound").EnumerateArray().Select(id => id.GetGuid()).ShouldBe([unknown]);
        (await ListAsync(client, marker)).Select(entry => entry.GetProperty("id").GetGuid()).ShouldBe([ids[2]]);

        // The entries are gone, so the plan the token was made for is no longer the plan.
        Code(replayed).ShouldBe("FCMS1633");
    }

    [Fact]
    public async Task ARunWhoseFilterMatchesOtherEntriesThanItsPlanIsRefusedAndNothingRuns()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var (marker, _) = await SeedAsync(client, "Planned", "Planned too");
        var arguments = new { type = "post", action = "delete", filter = new { note = new { eq = marker } }, limit = 5 };
        var token = TokenOf(await OkAsync(client, McpContentTools.Bulk, arguments));

        await OkAsync(client, "post_create_draft", new { fields = new { title = "Not planned", note = marker } });
        var refused = await CallAsync(client, McpContentTools.Bulk, Confirmed(arguments, token));
        var withoutAToken = await CallAsync(client, McpContentTools.Bulk, new { type = "post", action = "delete", filter = new { note = new { eq = marker } }, limit = 5, dryRun = false });
        var anotherLimit = await CallAsync(client, McpContentTools.Bulk, Confirmed(new { type = "post", action = "delete", filter = new { note = new { eq = marker } }, limit = 6 }, token));

        Code(refused).ShouldBe("FCMS1633");
        Code(withoutAToken).ShouldBe("FCMS1630");
        Code(anotherLimit).ShouldBe("FCMS1631");
        (await ListAsync(client, marker)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task ThePlanOfABulkDeleteNamesTheReferencesThatWouldBreakAndLeavesOutThoseOfThePlanItself()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var ada = (await OkAsync(client, "writer_create_draft", new { fields = new { name = "Ada" } })).GetProperty("id").GetGuid();
        var grace = (await OkAsync(client, "writer_create_draft", new { fields = new { name = "Grace" } })).GetProperty("id").GetGuid();
        var byAda = (await OkAsync(client, "post_create_draft", new { fields = new { title = "By Ada", author = ada } })).GetProperty("id").GetGuid();
        var byGrace = (await OkAsync(client, "post_create_draft", new { fields = new { title = "By Grace", author = grace } })).GetProperty("id").GetGuid();

        var writers = await OkAsync(client, McpContentTools.Bulk, new { type = "writer", action = "delete", ids = new[] { ada, grace }, limit = 2 });
        var published = await OkAsync(client, McpContentTools.Bulk, new { type = "writer", action = "publish", ids = new[] { ada }, limit = 1 });

        writers.GetProperty("references").EnumerateArray().Select(reference => reference.GetProperty("id").GetGuid()).ShouldBe([byAda, byGrace], ignoreOrder: true);
        writers.GetProperty("references").EnumerateArray().ShouldAllBe(reference => reference.GetProperty("type").GetString() == "post" && reference.GetProperty("field").GetString() == "author");
        published.GetProperty("references").GetArrayLength().ShouldBe(0);
    }

    // An unpublish takes the entry from the readers of what is published, so its plan names the published entries.
    [Fact]
    public async Task ThePlanOfABulkUnpublishNamesThePublishedEntriesThatPointToTheEntry()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var writer = (await OkAsync(client, "writer_create_draft", new { fields = new { name = "Ada" } })).GetProperty("id").GetGuid();
        var published = (await OkAsync(client, "post_create_draft", new { fields = new { title = "Out", author = writer } })).GetProperty("id").GetGuid();
        await OkAsync(client, "post_create_draft", new { fields = new { title = "Not out", author = writer } });
        await OkAsync(client, McpContentTools.Publish, new { type = "writer", id = writer });
        await OkAsync(client, McpContentTools.Publish, new { type = "post", id = published });
        var arguments = new { type = "writer", action = "unpublish", ids = new[] { writer }, limit = 1 };

        var plan = await OkAsync(client, McpContentTools.Bulk, arguments);
        var ran = await OkAsync(client, McpContentTools.Bulk, Confirmed(arguments, TokenOf(plan)));
        var replayed = await CallAsync(client, McpContentTools.Bulk, Confirmed(arguments, TokenOf(plan)));

        plan.GetProperty("entries")[0].GetProperty("status").GetString().ShouldBe("published");
        plan.GetProperty("references").EnumerateArray().Select(reference => reference.GetProperty("id").GetGuid()).ShouldBe([published]);
        ran.GetProperty("items")[0].GetProperty("status").GetString().ShouldBe("succeeded");
        (await OkAsync(client, "writer_get", new { id = writer })).GetProperty("status").GetString().ShouldBe("unpublished");

        // The status of the entry is part of the plan, so the run ended its own token.
        Code(replayed).ShouldBe("FCMS1633");
    }

    [Fact]
    public async Task BulkRefusesArgumentsItDoesNotTake()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var id = Guid.NewGuid();

        var both = await CallAsync(client, McpContentTools.Bulk, new { type = "post", action = "delete", ids = new[] { id }, filter = new { note = new { eq = "x" } }, limit = 1 });
        var neither = await CallAsync(client, McpContentTools.Bulk, new { type = "post", action = "delete", limit = 1 });
        var everything = await CallAsync(client, McpContentTools.Bulk, new { type = "post", action = "delete", filter = new { }, limit = 100 });
        var noAction = await CallAsync(client, McpContentTools.Bulk, new { type = "post", action = "purge", ids = new[] { id }, limit = 1 });
        var noType = await CallAsync(client, McpContentTools.Bulk, new { type = "nothing", action = "delete", ids = new[] { id }, limit = 1 });
        var localeOfADelete = await CallAsync(client, McpContentTools.Bulk, new { type = "post", action = "delete", ids = new[] { id }, limit = 1, locale = "en" });
        var patch = await CallAsync(client, McpContentTools.Bulk, new { type = "post", action = "delete", ids = new[] { id }, limit = 1, patch = new { title = "x" } });
        var noneFound = await OkAsync(client, McpContentTools.Bulk, new { type = "post", action = "delete", ids = new[] { id }, limit = 1 });

        Text(both).ShouldContain("- filter: ");
        Text(neither).ShouldContain("- ids: ");
        Text(everything).ShouldContain("- filter: ");
        Text(noAction).ShouldContain("- action: ");
        Code(noType).ShouldBe("FCMS1612");
        Text(localeOfADelete).ShouldContain("- locale: ");
        Text(patch).ShouldContain("- patch: ");
        new[] { both, neither, everything, noAction, localeOfADelete, patch }.ShouldAllBe(result => Code(result) == "FCMS2108");

        // With no entry there is nothing to confirm.
        noneFound.GetProperty("count").GetInt32().ShouldBe(0);
        noneFound.TryGetProperty("confirmationToken", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task BulkIsListedOnlyForACallerThatHoldsThePermissionOfAnAction()
    {
        var host = fixture.Host;
        using var reader = await RoleAsync(host, Grant("content.*.read"));
        using var deleter = await RoleAsync(host, Grant("content.*.read"), Grant("content.post.delete"));
        using var publisher = await RoleAsync(host, Grant("content.*.read"), Grant("content.writer.publish"), Grant("content.post.unpublish"));
        await using var asReader = await ConnectAsync(reader, protocolVersion: InitializeProtocol);
        await using var asDeleter = await ConnectAsync(deleter, protocolVersion: InitializeProtocol);
        await using var asPublisher = await ConnectAsync(publisher, protocolVersion: InitializeProtocol);

        var none = await ToolsAsync(asReader);
        var deletes = (await ToolsAsync(asDeleter))[McpContentTools.Bulk];
        var publishes = (await ToolsAsync(asPublisher))[McpContentTools.Bulk];

        none.Keys.ShouldNotContain(McpContentTools.Bulk);
        Enum(deletes.InputSchema, "action").ShouldBe(["delete"]);
        Enum(deletes.InputSchema, "type").ShouldBe(["post"]);
        Enum(publishes.InputSchema, "action").ShouldBe(["publish", "unpublish"]);
        Enum(publishes.InputSchema, "type").ShouldBe(["post", "writer"]);
        deletes.InputSchema.GetProperty("required").EnumerateArray().Select(name => name.GetString()).ShouldBe(["type", "action", "limit"]);
        deletes.InputSchema.GetProperty("properties").GetProperty("limit").GetProperty("maximum").GetInt32().ShouldBe(100);
    }

    // The service decides a call, whatever the list held: a caller without the permission of the action gets no entry changed.
    [Fact]
    public async Task ABulkActionTheCallerMayNotRunIsRefusedByTheService()
    {
        var host = fixture.Host;
        using var admin = await KeyAsync(host);
        using var http = await RoleAsync(host, Grant("content.*.read"), Grant("content.post.unpublish"));
        await using var adminClient = await ConnectAsync(admin, protocolVersion: InitializeProtocol);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var (marker, ids) = await SeedAsync(adminClient, "Not yours");
        var arguments = new { type = "post", action = "delete", ids, limit = 1 };

        var plan = await OkAsync(client, McpContentTools.Bulk, arguments);
        var ran = await CallAsync(client, McpContentTools.Bulk, Confirmed(arguments, TokenOf(plan)));

        (ran.IsError == true ? Code(ran) : ran.StructuredContent!.Value.GetProperty("items")[0].GetProperty("problem").GetProperty("code").GetString()).ShouldBe("FCMS3103");
        (await ListAsync(adminClient, marker)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task TheToolsWithADryRunAreDestructiveAndSayThatAConfirmationIsNeeded()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var tools = await ToolsAsync(client);

        foreach (var name in (string[])["post_delete", "writer_delete", McpContentTools.Bulk])
        {
            var tool = tools[name];
            tool.Annotations.ShouldNotBeNull().DestructiveHint.ShouldBe(true, name);
            tool.Annotations!.ReadOnlyHint.ShouldBe(false, name);
            tool.Description.ShouldNotBeNull().ShouldContain("A confirmation is needed", Case.Sensitive, name);
            var properties = tool.InputSchema.GetProperty("properties");
            properties.GetProperty("dryRun").GetProperty("default").GetBoolean().ShouldBeTrue(name);
            properties.GetProperty("confirmationToken").GetProperty("type").GetString().ShouldBe("string", name);
        }
    }

    private static List<string?> Enum(JsonElement schema, string property) =>
        [.. schema.GetProperty("properties").GetProperty(property).GetProperty("enum").EnumerateArray().Select(value => value.GetString())];

    // Posts that only this test reads, by a note of its own.
    private static async Task<(string Marker, Guid[] Ids)> SeedAsync(McpClient client, params string[] titles)
    {
        var marker = "m" + Guid.NewGuid().ToString("N");
        List<Guid> ids = [];
        foreach (var title in titles)
        {
            ids.Add((await OkAsync(client, "post_create_draft", new { fields = new { title, note = marker } })).GetProperty("id").GetGuid());
        }

        return (marker, [.. ids]);
    }

    private static async Task<List<JsonElement>> ListAsync(McpClient client, string marker) =>
        [.. (await OkAsync(client, "post_list", new { filter = new { note = new { eq = marker } } })).GetProperty("items").EnumerateArray()];
}
