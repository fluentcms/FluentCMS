using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Persistence;

namespace FluentCMS.IntegrationTests.Mcp;

public sealed class CheckToolsTests
{
    private static readonly JsonObject S2Draft = new()
    {
        ["headline"] = "The best in class kettle",
        ["body"] = "![](/media/kettle.jpg)",
        ["price"] = 199,
    };

    // S2 over MCP (CHK-04): the agent checks its draft before proposing it, fixes the three findings and the draft passes.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_checks_a_draft_fixes_what_three_checks_find_and_the_draft_passes(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        await ReferenceChecksAsync(agent);

        var failing = await agent.CallAsync("check_draft", new() { ["content_type"] = "product-page", ["fields"] = S2Draft });
        var fixedDraft = await agent.CallAsync("check_draft", new()
        {
            ["content_type"] = "product-page",
            ["fields"] = new JsonObject
            {
                ["headline"] = "A kettle that boils in two minutes",
                ["body"] = "![A steel kettle on a stove](/media/kettle.jpg)",
                ["price"] = 199,
            },
            ["sources"] = new JsonArray("exports/catalog-2026-10-18.csv"),
        });

        Assert.Equal("fail", (string?)failing["status"]);
        var results = failing["results"]!.AsArray();
        Assert.Equal(["alt-text", "no-superlatives", "sourced-prices"], results.Select(result => (string?)result!["check"]));
        Assert.All(results, result => Assert.Equal(("fail", "block", "deterministic"), ((string?)result!["status"], (string?)result["severity"], (string?)result["kind"])));
        var terms = results[1]!["findings"]!.AsArray().Single()!;
        Assert.Equal(
            ("Remove or rewrite 'best in class' in headline; 'best in class' is on this check's list of terms not to use.", "/headline", "best in class"),
            ((string?)terms["message"], (string?)terms["pointer"], (string?)terms["excerpt"]));
        Assert.Equal("/body", (string?)results[0]!["findings"]![0]!["pointer"]);
        Assert.StartsWith("Cite a source for the change to price", (string?)results[2]!["findings"]![0]!["message"]);
        Assert.Empty(failing["fieldErrors"]!.AsArray());

        Assert.Equal("pass", (string?)fixedDraft["status"]);
        Assert.All(fixedDraft["results"]!.AsArray(), result => Assert.Equal("pass", (string?)result!["status"]));

        // A check stores nothing and opens no set.
        Assert.Equal(0, await agent.QueryAsync(context => Task.FromResult(context.ChangeSets.Count())));
        Assert.Equal(0, await agent.QueryAsync(context => Task.FromResult(context.Entries.Count())));
    }

    // Only active checks run. Without a model an LLM-judged check cannot be activated (CHK-02, MDL-03), and an agent can always ask
    // for the deterministic checks alone.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_check_that_is_not_active_does_not_run_and_deterministic_checks_can_run_alone(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        await ReferenceChecksAsync(agent);
        await agent.AsPersonAsync<ICheckDirectory, Check>(directory => directory.CreateAsync(new NewCheck("calm-voice", new CheckDefinition(
            "tone",
            "Product copy is calm.",
            Parse("""{"voice": "Calm and factual."}"""),
            [
                new CheckExample("calm", CheckVerdict.Pass, Parse("""{"headline": "A kettle"}""")),
                new CheckExample("loud", CheckVerdict.Fail, Parse("""{"headline": "BUY!!!"}""")),
            ]))));

        // Stored, not activated: without a model its examples cannot be decided, so the run sees only active checks.
        var all = await agent.CallAsync("check_draft", new() { ["content_type"] = "product-page", ["fields"] = new JsonObject { ["headline"] = "A kettle" } });

        Assert.Equal("pass", (string?)all["status"]);
        Assert.DoesNotContain(all["results"]!.AsArray(), result => (string?)result!["check"] == "calm-voice");

        var deterministic = await agent.CallAsync("check_draft", new()
        {
            ["content_type"] = "product-page",
            ["fields"] = new JsonObject { ["headline"] = "A kettle" },
            ["checks"] = "deterministic",
        });
        Assert.Equal(3, deterministic["results"]!.AsArray().Count);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_draft_the_content_type_refuses_is_checked_and_its_field_errors_listed(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        await ReferenceChecksAsync(agent);

        var checkedDraft = await agent.CallAsync("check_draft", new()
        {
            ["content_type"] = "product-page",
            ["fields"] = new JsonObject { ["headline"] = 5, ["tagline"] = "Extra" },
        });

        Assert.Equal(
            ["field_unknown", "value_invalid"],
            checkedDraft["fieldErrors"]!.AsArray().Select(error => (string?)error!["code"]).Order());
        Assert.Equal(3, checkedDraft["results"]!.AsArray().Count);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_unknown_content_type_or_selection_is_refused_with_what_to_change(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);

        var unknown = await agent.FailAsync("check_draft", new() { ["content_type"] = "landing-page", ["fields"] = new JsonObject() });
        var selection = await agent.FailAsync("check_draft", new()
        {
            ["content_type"] = "landing-page",
            ["fields"] = new JsonObject(),
            ["checks"] = "some",
            ["draft"] = true,
        });

        Assert.Equal(SchemaErrorCodes.ContentTypeNotFound, (string?)unknown["errors"]![0]!["code"]);
        Assert.Equal("Call list_content_types for the slugs of the site's content types.", (string?)unknown["hint"]);
        Assert.Equal(
            ["argument_unknown", "argument_invalid"],
            selection["errors"]!.AsArray().Select(error => (string?)error!["code"]));
    }

    // The checks of the reference scenario, activated by a person, on a product page with a required headline.
    private static async Task ReferenceChecksAsync(ConnectedAgent agent)
    {
        await agent.AsPersonAsync<IContentTypeDirectory, ContentType>(directory => directory.CreateAsync(new NewContentType(
            "Product page",
            "product-page",
            new ContentTypeDefinition(
            [
                new FieldDefinition("headline", "text", required: true),
                new FieldDefinition("body", "richText"),
                new FieldDefinition("price", "number"),
            ]))));

        var checks = new Dictionary<string, CheckDefinition>
        {
            ["no-superlatives"] = Definition("terms", """{"terms": ["best in class"]}""", """{"headline": "A kettle"}""", """{"headline": "Best in class"}"""),
            ["alt-text"] = Definition("altText", "{}", """{"body": "![A kettle](/k.jpg)"}""", """{"body": "![](/k.jpg)"}"""),
            ["sourced-prices"] = Definition("sources", "{}", """{"headline": "A kettle"}""", """{"price": 199}""", ["price"]),
        };

        foreach (var (name, definition) in checks)
        {
            await agent.AsPersonAsync<ICheckDirectory, Check>(directory => directory.CreateAsync(new NewCheck(name, definition)));
            await agent.AsPersonAsync<ICheckDirectory, Check>(directory => directory.ActivateAsync(name, 1));
        }
    }

    private static CheckDefinition Definition(string type, string settings, string passing, string failing, IReadOnlyList<string>? fields = null)
    {
        return new CheckDefinition(
            type,
            $"The {type} check of the reference scenario.",
            Parse(settings),
            [
                new CheckExample("passes", CheckVerdict.Pass, Parse(passing), contentType: "product-page"),
                new CheckExample("fails", CheckVerdict.Fail, Parse(failing), contentType: "product-page"),
            ],
            scope: new CheckScope(["product-page"], fields));
    }

    private static JsonElement Parse(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(json);
    }
}
