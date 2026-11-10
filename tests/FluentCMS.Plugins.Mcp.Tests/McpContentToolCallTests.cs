using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Activity;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Testing;
using ModelContextProtocol;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Mcp.Tests.McpTesting;
using static FluentCMS.Plugins.Mcp.Tests.McpToolsTesting;

namespace FluentCMS.Plugins.Mcp.Tests;

// The content tools called: each goes to the content service as the caller and answers the JSON of the Management API.
public sealed class McpContentToolCallTests(McpToolsFixture fixture) : IClassFixture<McpToolsFixture>
{
    private const string Content = "/api/management/v1/content";

    private static readonly string[] Author = ["author"];

    [Fact]
    public async Task AnAgentWithoutThePublishPermissionDraftsAnEntryDoesNotSeePublishAndIsRefusedWhenItCallsIt()
    {
        using var http = await RoleAsync(fixture.Host, Grant("content.post.read"), Grant("content.post.create"), Grant("content.post.update"));
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var tools = await ToolsAsync(client);
        var draft = await OkAsync(client, "post_create_draft", new { fields = new { title = "By an agent" } });
        var refused = await CallAsync(client, McpContentTools.Publish, new { type = "post", id = draft.GetProperty("id").GetGuid() });

        tools.Keys.ShouldContain("post_create_draft");
        tools.Keys.ShouldNotContain(McpContentTools.Publish);
        tools.Keys.ShouldNotContain(McpContentTools.Schedule);
        draft.GetProperty("status").GetString().ShouldBe("draft");
        refused.IsError.ShouldBe(true);
        Code(refused).ShouldBe("FCMS3103");
        using var admin = await KeyAsync(fixture.Host);
        (await admin.GetFromJsonAsync<JsonNode>(new Uri($"{Content}/post/{draft.GetProperty("id").GetGuid():D}", UriKind.Relative), Token))!["status"]!.GetValue<string>().ShouldBe("draft");
    }

    [Fact]
    public async Task ATypeToolCalledByNameWithoutItsPermissionIsRefusedByTheService()
    {
        using var http = await RoleAsync(fixture.Host, Grant("content.post.read"));
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var created = await CallAsync(client, "post_create_draft", new { fields = new { title = "Not allowed" } });
        var deleted = await CallAsync(client, "post_delete", new { id = Guid.NewGuid() });

        Code(created).ShouldBe("FCMS3103");
        deleted.IsError.ShouldBe(true);
    }

    // A type the caller may not read has no tools, as a type that does not exist has none.
    [Fact]
    public async Task AToolOfATypeTheCallerMayNotReadIsNoTool()
    {
        using var http = await RoleAsync(fixture.Host, Grant("content.post.read"));
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var unreadable = await Should.ThrowAsync<McpProtocolException>(async () => await CallAsync(client, "writer_list"));
        var unknown = await Should.ThrowAsync<McpProtocolException>(async () => await CallAsync(client, "nothing_list"));
        var shared = await CallAsync(client, McpContentTools.Query, new { type = "writer" });

        unreadable.Message.ShouldContain("FCMS1600");
        unreadable.Message.ShouldBe(unknown.Message.Replace("nothing_list", "writer_list", StringComparison.Ordinal));
        Code(shared).ShouldBe("FCMS1612");
    }

    [Fact]
    public async Task TheCreateSchemaMatchesTheFieldsAndValidationOfTheTypeAndLeavesOutWhatTheCallerMayNotWrite()
    {
        using var http = await RoleAsync(
            fixture.Host,
            Grant("content.*.read"),
            Grant("content.post.create", "title", "category", "rating", "author", "seo", "sections", "internal", "imported"),
            Grant("content.post.update", "title"));
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var tools = await ToolsAsync(client);

        var schema = tools["post_create_draft"].InputSchema;
        schema.GetProperty("required").EnumerateArray().Select(name => name.GetString()).ShouldBe(["fields"]);
        var fields = schema.GetProperty("properties").GetProperty("fields");
        fields.GetProperty("additionalProperties").GetBoolean().ShouldBeFalse();
        var properties = fields.GetProperty("properties");
        properties.EnumerateObject().Select(property => property.Name).ShouldBe(["title", "category", "rating", "author", "seo", "sections"]);

        var title = properties.GetProperty("title");
        title.GetProperty("type").GetString().ShouldBe("string");
        title.GetProperty("maxLength").GetInt32().ShouldBe(20);
        title.GetProperty("description").GetString().ShouldBe("The headline. Required to publish.");
        properties.GetProperty("category").GetProperty("enum").EnumerateArray().Select(option => option.GetString()).ShouldBe(["news", "opinion"]);
        properties.GetProperty("rating").ShouldSatisfyAllConditions(
            rating => rating.GetProperty("type").GetString().ShouldBe("integer"),
            rating => rating.GetProperty("minimum").GetInt32().ShouldBe(1),
            rating => rating.GetProperty("maximum").GetInt32().ShouldBe(5));
        properties.GetProperty("author").ShouldSatisfyAllConditions(
            author => author.GetProperty("format").GetString().ShouldBe("uuid"),
            author => author.GetProperty("description").GetString().ShouldBe("The id of a writer entry."));
        properties.GetProperty("seo").GetProperty("$ref").GetString().ShouldBe("#/$defs/component-seo");
        properties.GetProperty("sections").GetProperty("items").GetProperty("oneOf")[0].GetProperty("$ref").GetString().ShouldBe("#/$defs/block-quote");
        properties.GetProperty("sections").GetProperty("items").TryGetProperty("discriminator", out _).ShouldBeFalse();

        var definitions = schema.GetProperty("$defs");
        definitions.GetProperty("component-seo").GetProperty("properties").GetProperty("metaTitle").GetProperty("maxLength").GetInt32().ShouldBe(60);
        definitions.GetProperty("block-quote").GetProperty("properties").GetProperty("type").GetProperty("const").GetString().ShouldBe("quote");
        definitions.GetProperty("block-quote").GetProperty("properties").TryGetProperty("text", out _).ShouldBeTrue();

        var update = tools["post_update_draft"].InputSchema.GetProperty("properties");
        update.GetProperty("fields").GetProperty("properties").EnumerateObject().Select(property => property.Name).ShouldBe(["title"]);
        tools["post_update_draft"].InputSchema.GetProperty("required").EnumerateArray().Select(name => name.GetString()).ShouldBe(["id", "expectedVersion", "fields"]);
    }

    [Fact]
    public async Task AFieldTheCallerMayNotReadIsInNoSchemaAndNoAnswer()
    {
        using var http = await RoleAsync(fixture.Host, Grant("content.post.read", "title", "rating"), Grant("content.post.create"), Grant("content.post.update"));
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var tools = await ToolsAsync(client);
        var created = await OkAsync(client, "post_create_draft", new { fields = new { title = "Half seen", rating = 2 } });

        tools["post_create_draft"].InputSchema.GetProperty("properties").GetProperty("fields").GetProperty("properties")
            .EnumerateObject().Select(property => property.Name).ShouldBe(["title", "rating"]);
        tools["post_list"].Description.ShouldNotBeNull().ShouldNotContain("note");
        tools["post_list"].Description!.ShouldContain("title (text)");
        created.TryGetProperty("note", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task AValidationFailureIsAToolErrorWithItsCodeAndItsFieldPaths()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var tooLong = await CallAsync(client, "post_create_draft", new { fields = new { title = new string('x', 40), category = "sports" } });
        var noFields = await CallAsync(client, "post_create_draft", new { title = "Outside fields" });
        var badId = await CallAsync(client, "post_get", new { id = "seven" });

        tooLong.IsError.ShouldBe(true);
        Code(tooLong).ShouldBe("FCMS2100");
        Text(tooLong).ShouldContain("- title: ");
        Text(tooLong).ShouldContain("(FCMS2103)");
        Text(tooLong).ShouldContain("- category: ");
        Text(tooLong).ShouldContain("(FCMS2107)");
        Text(tooLong).ShouldNotContain("   at ");
        Code(noFields).ShouldBe("FCMS2108");
        Text(noFields).ShouldContain("- title: ");
        Code(badId).ShouldBe("FCMS2108");
        Text(badId).ShouldContain("- id: ");
    }

    [Fact]
    public async Task AnEntryIsCreatedReadUpdatedAndDeletedThroughItsTools()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var writer = (await OkAsync(client, "writer_create_draft", new { fields = new { name = "Ada" } })).GetProperty("id").GetGuid();

        var created = await OkAsync(client, "post_create_draft", new
        {
            fields = new { title = "First", category = "news", rating = 4, author = writer, seo = new { metaTitle = "First post" }, sections = new[] { new { type = "quote", text = "Said" } } },
        });
        var id = created.GetProperty("id").GetGuid();
        var read = await OkAsync(client, "post_get", new { id, populate = Author });
        var updated = await OkAsync(client, "post_update_draft", new { id, expectedVersion = 1, fields = new { title = "Second", category = (string?)null } });
        var stale = await CallAsync(client, "post_update_draft", new { id, expectedVersion = 1, fields = new { title = "Third" } });
        var deleted = await ConfirmedAsync(client, "post_delete", new { id });
        var gone = await CallAsync(client, "post_get", new { id });

        created.GetProperty("version").GetInt32().ShouldBe(1);
        read.GetProperty("author").GetProperty("name").GetString().ShouldBe("Ada");
        read.GetProperty("seo").GetProperty("metaTitle").GetString().ShouldBe("First post");
        read.GetProperty("sections")[0].GetProperty("text").GetString().ShouldBe("Said");
        updated.GetProperty("title").GetString().ShouldBe("Second");
        updated.GetProperty("rating").GetInt32().ShouldBe(4);
        updated.GetProperty("category").ValueKind.ShouldBe(JsonValueKind.Null);
        updated.GetProperty("version").GetInt32().ShouldBe(2);
        Code(stale).ShouldBe("FCMS3105");
        deleted.GetProperty("deleted").GetBoolean().ShouldBeTrue();
        deleted.GetProperty("id").GetGuid().ShouldBe(id);
        Code(gone).ShouldBe("FCMS3104");
    }

    [Fact]
    public async Task TheAnswerOfAToolIsTheJsonOfTheManagementApi()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var id = (await OkAsync(client, "post_create_draft", new { fields = new { title = "Same shape", rating = 3 } })).GetProperty("id").GetGuid();

        var fromTool = await OkAsync(client, "post_get", new { id });
        var fromApi = await http.GetFromJsonAsync<JsonNode>(new Uri($"{Content}/post/{id:D}", UriKind.Relative), Token);

        JsonNode.DeepEquals(JsonNode.Parse(fromTool.GetRawText()), fromApi).ShouldBeTrue(fromTool.GetRawText());
    }

    [Fact]
    public async Task AnEntryIsPublishedScheduledUnpublishedAndItsDraftDiscarded()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var id = (await OkAsync(client, "post_create_draft", new { fields = new { title = "To publish" } })).GetProperty("id").GetGuid();

        var published = await OkAsync(client, McpContentTools.Publish, new { type = "post", id, expectedVersion = 1 });
        var edited = await OkAsync(client, "post_update_draft", new { id, expectedVersion = 1, fields = new { title = "Edited" } });
        var discarded = await OkAsync(client, "post_discard_draft", new { id });
        var scheduled = await OkAsync(client, McpContentTools.Schedule, new { type = "post", id, action = "unpublish", at = "2999-03-01T09:00:00Z" });
        var badTime = await CallAsync(client, McpContentTools.Schedule, new { type = "post", id, action = "unpublish", at = "tomorrow" });
        var unpublished = await OkAsync(client, McpContentTools.Unpublish, new { type = "post", id });

        published.GetProperty("entry").GetProperty("status").GetString().ShouldBe("published");
        published.GetProperty("warnings").GetArrayLength().ShouldBe(0);
        edited.GetProperty("status").GetString().ShouldBe("modified");
        discarded.GetProperty("title").GetString().ShouldBe("To publish");
        discarded.GetProperty("status").GetString().ShouldBe("published");
        scheduled.GetProperty("action").GetString().ShouldBe("unpublish");
        scheduled.GetProperty("runAt").GetString().ShouldBe("2999-03-01T09:00:00Z");
        Code(badTime).ShouldBe("FCMS2108");
        Text(badTime).ShouldContain("- at: ");
        unpublished.GetProperty("status").GetString().ShouldBe("unpublished");
    }

    [Fact]
    public async Task ASingletonIsCreatedReadUpdatedAndDiscardedWithoutAnId()
    {
        await using var host = Host(Path.GetTempPath(), new FakeDownloads());
        using var http = await KeyAsync(host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var empty = await CallAsync(client, "masthead_get");
        var noEntry = await CallAsync(client, "masthead_update_draft", new { expectedVersion = 3, fields = new { tagline = "Too soon" } });
        var created = await OkAsync(client, "masthead_update_draft", new { expectedVersion = 0, fields = new { tagline = "All the news" } });
        var read = await OkAsync(client, "masthead_get");
        var updated = await OkAsync(client, "masthead_update_draft", new { expectedVersion = 1, fields = new { tagline = "Most of the news" } });
        await OkAsync(client, McpContentTools.Publish, new { type = "masthead", id = created.GetProperty("id").GetGuid() });
        await OkAsync(client, "masthead_update_draft", new { expectedVersion = 2, fields = new { tagline = "A typo" } });
        var discarded = await OkAsync(client, "masthead_discard_draft");
        var noList = await Should.ThrowAsync<McpProtocolException>(async () => await CallAsync(client, "masthead_list"));

        Code(empty).ShouldBe("FCMS1613");
        Code(noEntry).ShouldBe("FCMS1613");
        read.GetProperty("tagline").GetString().ShouldBe("All the news");
        read.GetProperty("id").GetGuid().ShouldBe(created.GetProperty("id").GetGuid());
        updated.GetProperty("version").GetInt32().ShouldBe(2);
        discarded.GetProperty("tagline").GetString().ShouldBe("Most of the news");
        noList.Message.ShouldContain("FCMS1600");
    }

    [Fact]
    public async Task EachWriteThroughAToolHasTheMcpOriginAndTheClientOnItsVersionAndItsActivityRow()
    {
        var host = fixture.Host;
        var key = await host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.EditorName, cancellationToken: Token);
        using var http = host.AsKey(key);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var id = (await OkAsync(client, "post_create_draft", new { fields = new { title = "Tagged" } })).GetProperty("id").GetGuid();
        await OkAsync(client, "post_update_draft", new { id, expectedVersion = 1, fields = new { title = "Tagged twice" } });
        await OkAsync(client, McpContentTools.Publish, new { type = "post", id });

        var admin = host.As(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));
        var versions = await admin.Content<Post>().GetVersionsAsync(id, cancellationToken: Token);
        versions.Count.ShouldBe(2);
        versions.ShouldAllBe(version => version.Origin == Origin.Mcp && version.ClientId == key.Id.ToString() && version.AuthorId == key.Id);

        await DrainOutboxAsync(host);
        var rows = await admin.RunAsync<IActivityQuery, PagedResult<ActivityItem>>(query => query.QueryAsync(new ActivityFilter { TargetType = "post", TargetId = id.ToString() }, null, Token));
        rows.Items.Count.ShouldBe(3);
        rows.Items.ShouldAllBe(row => row.Origin == Origin.Mcp && row.ClientId == key.Id.ToString() && row.ActorId == key.Id);
    }
}
