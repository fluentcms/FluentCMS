using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Agents;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.IntegrationTests.Mcp;

public sealed class EntryToolsTests
{
    // S2 in the F-CNT acceptance, end to end over MCP.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_proposes_a_product_and_a_page_that_shows_its_price_and_reads_them_once_published(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        await CatalogAsync(agent);

        var opened = await agent.CallAsync("open_change_set", new() { ["rationale"] = "Launch the Widget.", ["sources"] = new[] { "https://example.com/widget" } });
        var setId = (string)opened["changeSet"]!["id"]!;
        var product = await agent.CallAsync("create_entry", new() { ["content_type"] = "product", ["fields"] = Product("19.99"), ["change_set_id"] = setId });
        var productId = (string)product["entry"]!["id"]!;
        var page = await agent.CallAsync("create_entry", new()
        {
            ["content_type"] = "product-page",
            ["fields"] = new JsonObject { ["headline"] = "The Widget", ["price"] = productId },
            ["change_set_id"] = setId,
        });
        var pageId = (string)page["entry"]!["id"]!;
        await agent.CallAsync("submit_change_set", new() { ["id"] = setId });

        Assert.Equal(["entity", "entry"], page["changeSet"]!["operations"]!.AsArray().Select(operation => (string?)operation!["itemKind"]));
        Assert.Contains("submit_change_set", (string?)page["next"]);
        Assert.Equal("in_review", (string?)(await agent.CallAsync("get_entry", new() { ["id"] = pageId }))["state"]);

        await PublishAsPersonAsync(agent, setId);

        var resolved = await agent.CallAsync("get_resolved_entry", new() { ["id"] = pageId });
        var usages = await agent.CallAsync("get_where_used", new() { ["entry_id"] = productId, ["field"] = "price" });
        var listed = await agent.CallAsync("list_entries", new() { ["content_type"] = "product-page" });
        var stored = await agent.CallAsync("get_entry", new() { ["id"] = pageId });
        var history = await agent.CallAsync("get_entry_history", new() { ["id"] = pageId });

        Assert.Equal(19.99m, (decimal)resolved["fields"]!["price"]!);
        Assert.Empty(resolved["unresolved"]!.AsArray());
        var usage = Assert.Single(usages["usages"]!.AsArray())!;
        Assert.Equal(pageId, (string?)usage["entryId"]);
        Assert.Equal(["/price"], usage["paths"]!.AsArray().Select(path => (string?)path));
        Assert.Equal("published", (string?)Assert.Single(listed["entries"]!.AsArray())!["state"]);
        Assert.Equal(productId, (string?)stored["fields"]!["price"]);
        Assert.Equal(1, (int)stored["publishedVersion"]!);
        Assert.Equal([1], history["versions"]!.AsArray().Select(version => (int)version!["version"]!));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_update_is_based_on_the_published_version_and_a_refused_one_stores_nothing(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        await CatalogAsync(agent);
        var setId = (string)(await agent.CallAsync("open_change_set", new() { ["rationale"] = "Launch." }))["changeSet"]!["id"]!;
        var productId = (string)(await agent.CallAsync("create_entry", new() { ["content_type"] = "product", ["fields"] = Product("19.99"), ["change_set_id"] = setId }))["entry"]!["id"]!;
        await agent.CallAsync("submit_change_set", new() { ["id"] = setId });
        await PublishAsPersonAsync(agent, setId);

        var update = await agent.CallAsync("update_entry", new()
        {
            ["id"] = productId,
            ["base_version"] = 1,
            ["fields"] = Product("24.99"),
            ["session_id"] = "price-update",
            ["rationale"] = "New price list.",
        });
        var stale = await agent.FailAsync("update_entry", new()
        {
            ["id"] = productId,
            ["base_version"] = 1,
            ["fields"] = Product("29.99"),
            ["session_id"] = "price-update-2",
            ["rationale"] = "Another price.",
        });
        var invalid = await agent.FailAsync("update_entry", new()
        {
            ["id"] = productId,
            ["base_version"] = 2,
            ["fields"] = new JsonObject { ["nmae"] = "Typo", ["price"] = "free" },
            ["session_id"] = "price-update-3",
            ["rationale"] = "Broken fields.",
        });

        var operation = Assert.Single(update["changeSet"]!["operations"]!.AsArray())!;
        Assert.Equal(("update", "entity", 1, 2), ((string?)operation["kind"], (string?)operation["itemKind"], (int?)operation["baseVersion"], (int?)operation["proposedVersion"]));
        Assert.Equal(ContentErrorCodes.VersionConflict, Code(stale));
        Assert.Contains("get_entry", (string?)stale["hint"]);
        Assert.True(invalid["errors"]!.AsArray().Count >= 2, invalid.ToJsonString());
        Assert.Contains("get_schema_document", (string?)invalid["hint"]);
        Assert.Equal(2, await agent.QueryAsync(context => context.EntryVersions.CountAsync()));
        Assert.Equal(2, await agent.QueryAsync(context => context.ChangeSets.CountAsync()));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Delete_proposes_unpublishing_a_published_entry_only(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(
            provider,
            operations: [AgentOperation.Read, AgentOperation.Create, AgentOperation.Update, AgentOperation.Delete]);
        await CatalogAsync(agent);
        var setId = (string)(await agent.CallAsync("open_change_set", new() { ["rationale"] = "Launch." }))["changeSet"]!["id"]!;
        var productId = (string)(await agent.CallAsync("create_entry", new() { ["content_type"] = "product", ["fields"] = Product("19.99"), ["change_set_id"] = setId }))["entry"]!["id"]!;

        var unpublished = await agent.FailAsync("delete_entry", new() { ["id"] = productId, ["session_id"] = "retire", ["rationale"] = "Retire the Widget." });
        Assert.Equal(ContentErrorCodes.EntryNotPublished, Code(unpublished));

        await agent.CallAsync("submit_change_set", new() { ["id"] = setId });
        await PublishAsPersonAsync(agent, setId);
        var delete = await agent.CallAsync("delete_entry", new() { ["id"] = productId, ["session_id"] = "retire", ["rationale"] = "Retire the Widget." });

        var operation = Assert.Single(delete["changeSet"]!["operations"]!.AsArray())!;
        Assert.Equal(("delete", 1), ((string?)operation["kind"], (int?)operation["baseVersion"]));
        Assert.Null(operation["proposedVersion"]);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_reads_resolves_and_finds_uses_only_within_its_scope(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider, types: ["product"]);
        await CatalogAsync(agent);
        var product = await agent.AsPersonAsync<IEntryDirectory, Entry>(entries => entries.CreateAsync(new NewEntry("product", Json(Product("19.99").ToJsonString()))));
        var page = await agent.AsPersonAsync<IEntryDirectory, Entry>(entries =>
            entries.CreateAsync(new NewEntry("product-page", Json($$"""{"headline": "The Widget", "price": "{{product.Id}}"}"""))));

        var get = await agent.FailAsync("get_entry", new() { ["id"] = page.Id.ToString() });
        var resolve = await agent.FailAsync("get_resolved_entry", new() { ["id"] = page.Id.ToString() });
        var usages = await agent.CallAsync("get_where_used", new() { ["entry_id"] = product.Id.ToString(), ["field"] = "price" });
        var neither = await agent.FailAsync("get_where_used", new() { ["field"] = "price" });

        Assert.Equal(AgentPermissionErrorCodes.ScopeDenied, Code(get));
        Assert.Equal(AgentPermissionErrorCodes.ScopeDenied, Code(resolve));
        Assert.Empty(usages["usages"]!.AsArray());
        Assert.Equal("argument_missing", Code(neither));
    }

    // A product entity and a product page whose price shows the product's, defined by a person.
    private static async Task CatalogAsync(ConnectedAgent agent)
    {
        await agent.AsPersonAsync<IContentTypeDirectory, ContentType>(directory => directory.CreateAsync(new NewContentType(
            "Product",
            "product",
            new ContentTypeDefinition([new FieldDefinition("name", "text", required: true), new FieldDefinition("price", "number")], isEntity: true))));
        await agent.AsPersonAsync<IContentTypeDirectory, ContentType>(directory => directory.CreateAsync(new NewContentType(
            "Product page",
            "product-page",
            new ContentTypeDefinition(
            [
                new FieldDefinition("headline", "text"),
                new FieldDefinition("price", "reference", settings: Json("""{"target": "product", "field": "price"}""")),
            ]))));
    }

    private static async Task PublishAsPersonAsync(ConnectedAgent agent, string setId)
    {
        var id = Guid.Parse(setId);
        await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.TransitionAsync(id, ChangeSetState.Approved));
        await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.PublishAsync(id));
    }

    private static JsonObject Product(string price)
    {
        static JsonObject Fact(JsonNode? value) => new()
        {
            ["value"] = value,
            ["source"] = null,
            ["owner"] = null,
            ["validFrom"] = null,
            ["validUntil"] = null,
            ["lastVerified"] = null,
        };

        return new JsonObject { ["name"] = Fact("Widget"), ["price"] = Fact(JsonNode.Parse(price)) };
    }

    private static string? Code(JsonObject failure)
    {
        return (string?)failure["errors"]![0]!["code"];
    }

    private static JsonElement Json(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(json);
    }
}
