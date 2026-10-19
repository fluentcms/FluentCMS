using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Core.Entities;
using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.IntegrationTests.Mcp;

public sealed class EntityToolsTests
{
    private static readonly JsonObject Export = new() { ["kind"] = "file", ["reference"] = "exports/catalog-2026-10-18.csv" };

    // S1 over MCP: the pipeline agent sets prices from the export in one session's set and reads which live pages they move.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_sets_prices_from_an_export_and_reads_the_pages_they_move(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        await CatalogAsync(agent);
        var (drill, saw, sander) = (await ProductAsync(agent, "99"), await ProductAsync(agent, "49"), await ProductAsync(agent, "79"));
        var (drillPage, sawPage, sanderPage) = (await PageAsync(agent, drill), await PageAsync(agent, saw), await PageAsync(agent, sander));
        await PublishAsync(agent, drill, saw, sander, drillPage, sawPage, sanderPage);

        string? setId = null;
        foreach (var (product, price) in new[] { (drill, 109m), (saw, 59m) })
        {
            var proposed = await agent.CallAsync("set_entity_fact", new()
            {
                ["id"] = product.ToString(),
                ["base_version"] = 1,
                ["field"] = "price",
                ["value"] = price,
                ["source"] = Export,
                ["session_id"] = "catalog-sync-2026-10-18",
                ["rationale"] = "Sync prices from the nightly export.",
            });
            setId = (string)proposed["changeSet"]!["id"]!;
            Assert.Equal("Drill", (string?)proposed["entry"]!["fields"]!["name"]!["value"]);
        }

        var radius = await agent.CallAsync("get_blast_radius", new() { ["change_set_id"] = setId });
        var facts = await agent.CallAsync("get_entity_facts", new() { ["id"] = drill.ToString() });

        var entries = radius["entries"]!.AsArray();
        Assert.Equal(new[] { drillPage, sawPage }.Order().Select(id => id.ToString()), entries.Select(entry => (string?)entry!["entryId"]));
        var drillDiff = Assert.Single(entries.Single(entry => (string?)entry!["entryId"] == drillPage.ToString())!["fields"]!.AsArray())!;
        Assert.Equal(("/price", 99m, 109m), ((string?)drillDiff["name"], (decimal)drillDiff["before"]!, (decimal)drillDiff["after"]!));
        Assert.DoesNotContain(entries, entry => (string?)entry!["entryId"] == sanderPage.ToString());

        var priceFact = facts["facts"]!.AsArray().Single(fact => (string?)fact!["field"] == "price")!;
        Assert.False((bool)priceFact["unsourced"]!);
        Assert.Equal("exports/catalog-2026-10-18.csv", (string?)priceFact["source"]!["reference"]);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_fact_set_without_a_source_is_flagged_and_listed(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        await CatalogAsync(agent);
        var drill = await ProductAsync(agent, "99");

        await agent.CallAsync("set_entity_fact", new()
        {
            ["id"] = drill.ToString(),
            ["base_version"] = 1,
            ["field"] = "price",
            ["value"] = 89,
            ["session_id"] = "guess",
            ["rationale"] = "A price heard on the phone.",
        });

        var facts = await agent.CallAsync("get_entity_facts", new() { ["id"] = drill.ToString() });
        var unsourced = await agent.CallAsync("list_unsourced_facts", new() { ["content_type"] = "product" });

        Assert.True((bool)facts["facts"]!.AsArray().Single(fact => (string?)fact!["field"] == "price")!["unsourced"]!);
        Assert.Contains(unsourced["facts"]!.AsArray(), flag => (string?)flag!["field"] == "price" && (int)flag["version"]! == 2);
    }

    // ENT-03: a price last verified long ago is listed until a re-verified version publishes.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_stale_price_is_listed_until_its_re_verification_publishes(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        await CatalogAsync(agent);
        var verified = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var drill = await CreateAsync(
            agent,
            "product",
            $$"""{"name": {{Fact("\"Drill\"")}}, "price": {"value": 99, "source": {"kind": "system", "reference": "seed"}, "owner": null, "validFrom": null, "validUntil": null, "lastVerified": "2025-01-02T03:04:05Z"} }""");
        await PublishAsync(agent, drill);

        var before = await agent.CallAsync("list_stale_facts", new() { ["content_type"] = "product" });
        var proposed = await agent.CallAsync("set_entity_fact", new()
        {
            ["id"] = drill.ToString(),
            ["base_version"] = 1,
            ["field"] = "price",
            ["value"] = 99,
            ["source"] = Export,
            ["last_verified"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["session_id"] = "verify-prices",
            ["rationale"] = "Checked against the export.",
        });
        var proposedOnly = await agent.CallAsync("list_stale_facts", new());
        var setId = (string)proposed["changeSet"]!["id"]!;
        await agent.CallAsync("submit_change_set", new() { ["id"] = setId });
        await PublishAsPersonAsync(agent, setId);
        var after = await agent.CallAsync("list_stale_facts", new());

        var fact = Assert.Single(before["facts"]!.AsArray())!;
        Assert.Equal(
            (drill.ToString(), "price", 1, "unverified", true),
            ((string?)fact["entityId"], (string?)fact["field"], (int)fact["version"]!, (string?)fact["reason"], (bool)fact["isPublished"]!));
        Assert.Equal(verified.AddDays(180), DateTimeOffset.Parse((string)fact["staleSince"]!, CultureInfo.InvariantCulture));
        Assert.Equal(1, (int)Assert.Single(proposedOnly["facts"]!.AsArray())!["version"]!);
        Assert.Empty(after["facts"]!.AsArray());
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_relates_a_product_to_a_spec_and_a_category_and_reads_both_directions(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        await CatalogAsync(agent);
        var drill = await ProductAsync(agent, "99");
        var tools = await EntityAsync(agent, "category", "Tools");
        var width = await EntityAsync(agent, "spec", "Width");

        await agent.CallAsync("relate_entities", new() { ["id"] = drill.ToString(), ["base_version"] = 1, ["field"] = "specs", ["related_id"] = width.ToString(), ["session_id"] = "relate", ["rationale"] = "Link the spec." });
        await agent.CallAsync("relate_entities", new() { ["id"] = drill.ToString(), ["base_version"] = 2, ["field"] = "category", ["related_id"] = tools.ToString(), ["session_id"] = "relate-category", ["rationale"] = "File the drill." });

        var product = await agent.CallAsync("get_entity_relations", new() { ["id"] = drill.ToString() });
        var spec = await agent.CallAsync("get_entity_relations", new() { ["id"] = width.ToString() });

        Assert.Equal(
            [("category", tools.ToString()), ("specs", width.ToString())],
            product["outgoing"]!.AsArray().Select(relation => ((string)relation!["field"]!, (string)relation["toId"]!)));
        Assert.Equal(drill.ToString(), (string?)Assert.Single(spec["incoming"]!.AsArray())!["fromId"]);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Refused_fact_and_relation_writes_say_why_and_store_nothing(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        await CatalogAsync(agent);
        var drill = await ProductAsync(agent, "99");
        var tools = await EntityAsync(agent, "category", "Tools");
        var page = await PageAsync(agent, drill);
        var versions = await agent.QueryAsync(context => context.EntryVersions.CountAsync());

        Dictionary<string, object?> Target(string session) => new() { ["session_id"] = session, ["rationale"] = "Refused." };
        var stale = await agent.FailAsync("set_entity_fact", new(Target("a")) { ["id"] = drill.ToString(), ["base_version"] = 2, ["field"] = "price", ["value"] = 1, ["source"] = Export });
        var unknownField = await agent.FailAsync("set_entity_fact", new(Target("b")) { ["id"] = drill.ToString(), ["base_version"] = 1, ["field"] = "colour", ["value"] = "red" });
        var plain = await agent.FailAsync("set_entity_fact", new(Target("c")) { ["id"] = page.ToString(), ["base_version"] = 1, ["field"] = "headline", ["value"] = "x" });
        var notRelation = await agent.FailAsync("relate_entities", new(Target("d")) { ["id"] = drill.ToString(), ["base_version"] = 1, ["field"] = "name", ["related_id"] = tools.ToString() });
        var wrongType = await agent.FailAsync("relate_entities", new(Target("e")) { ["id"] = drill.ToString(), ["base_version"] = 1, ["field"] = "specs", ["related_id"] = tools.ToString() });

        Assert.Equal(ContentErrorCodes.VersionNotFound, Code(stale));
        Assert.Equal(ContentErrorCodes.FieldUnknown, Code(unknownField));
        Assert.Contains("get_schema_document", (string?)unknownField["hint"]);
        Assert.Equal(ContentErrorCodes.EntryNotEntity, Code(plain));
        Assert.Equal(EntityErrorCodes.FieldNotRelation, Code(notRelation));
        Assert.Contains("get_content_type", (string?)notRelation["hint"]);
        Assert.Equal(ContentErrorCodes.ReferenceTypeMismatch, Code(wrongType));
        Assert.Equal(versions, await agent.QueryAsync(context => context.EntryVersions.CountAsync()));
        Assert.Equal(0, await agent.QueryAsync(context => context.ChangeSets.CountAsync()));
    }

    private static async Task CatalogAsync(ConnectedAgent agent)
    {
        async Task TypeAsync(string slug, bool isEntity, params FieldDefinition[] fields)
        {
            await agent.AsPersonAsync<IContentTypeDirectory, ContentType>(directory =>
                directory.CreateAsync(new NewContentType(slug, slug, new ContentTypeDefinition(fields, isEntity))));
        }

        await TypeAsync("category", true, new FieldDefinition("name", "text"));
        await TypeAsync("spec", true, new FieldDefinition("name", "text"));
        await TypeAsync(
            "product",
            true,
            new FieldDefinition("name", "text"),
            new FieldDefinition("price", "number"),
            new FieldDefinition("category", "reference", settings: Json("""{"target": "category"}""")),
            new FieldDefinition("specs", "list", settings: Json("""{"itemType": "reference", "itemSettings": {"target": "spec"}}""")));
        await TypeAsync(
            "product-page",
            false,
            new FieldDefinition("headline", "text"),
            new FieldDefinition("price", "reference", settings: Json("""{"target": "product", "field": "price"}""")));
    }

    private static Task<Guid> ProductAsync(ConnectedAgent agent, string price)
    {
        return CreateAsync(agent, "product", $$"""{"name": {{Fact("\"Drill\"")}}, "price": {{Fact(price)}}}""");
    }

    private static Task<Guid> EntityAsync(ConnectedAgent agent, string type, string name)
    {
        return CreateAsync(agent, type, $$"""{"name": {{Fact($"\"{name}\"")}}}""");
    }

    private static Task<Guid> PageAsync(ConnectedAgent agent, Guid product)
    {
        return CreateAsync(agent, "product-page", $$"""{"headline": "Buy it", "price": "{{product}}"}""");
    }

    private static async Task<Guid> CreateAsync(ConnectedAgent agent, string type, string fields)
    {
        var entry = await agent.AsPersonAsync<IEntryDirectory, Entry>(entries => entries.CreateAsync(new NewEntry(type, Json(fields))));
        return entry.Id;
    }

    // Publish moves the pointer through change sets (#2519); these tests only need live pages and entities.
    private static Task<int> PublishAsync(ConnectedAgent agent, params Guid[] entries)
    {
        return agent.QueryAsync(context => context.Entries
            .Where(row => entries.Contains(row.Id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PublishedVersion, 1)));
    }

    private static async Task PublishAsPersonAsync(ConnectedAgent agent, string setId)
    {
        var id = Guid.Parse(setId);
        await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.TransitionAsync(id, ChangeSetState.Approved));
        await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.PublishAsync(id));
    }

    private static string Fact(string value)
    {
        return $$"""{"value": {{value}}, "source": {"kind": "system", "reference": "seed"}, "owner": null, "validFrom": null, "validUntil": null, "lastVerified": null}""";
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
