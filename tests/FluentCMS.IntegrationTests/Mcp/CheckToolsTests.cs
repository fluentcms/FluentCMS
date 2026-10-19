using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Checks;
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

        var failing = await agent.CallAsync("check_draft", new() { ["content_type"] = "product-page", ["fields"] = S2Draft.DeepClone() });
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

    // MDL-03: an LLM-judged check activated while a model was configured needs review once there is none, and the run does too;
    // an agent that asks for the deterministic checks alone is told which ones it left out.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Without_a_model_an_active_LLM_judged_check_needs_review_and_can_be_left_out(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        await ReferenceChecksAsync(agent);
        await ActiveWithoutItsGateAsync(agent, "calm-voice", new CheckDefinition(
            "tone",
            "Product copy is calm.",
            Parse("""{"voice": "Calm and factual."}"""),
            [
                new CheckExample("calm", CheckVerdict.Pass, Parse("""{"headline": "A kettle"}""")),
                new CheckExample("loud", CheckVerdict.Fail, Parse("""{"headline": "BUY!!!"}""")),
            ]));
        var draft = new JsonObject { ["headline"] = "A kettle" };

        var all = await agent.CallAsync("check_draft", new() { ["content_type"] = "product-page", ["fields"] = draft.DeepClone() });
        var deterministic = await agent.CallAsync("check_draft", new()
        {
            ["content_type"] = "product-page",
            ["fields"] = draft.DeepClone(),
            ["checks"] = "deterministic",
        });

        Assert.Equal("needs_review", (string?)all["status"]);
        var judged = all["results"]!.AsArray().Single(result => (string?)result!["check"] == "calm-voice")!;
        Assert.Equal(("needs_review", "llm_judged"), ((string?)judged["status"], (string?)judged["kind"]));
        Assert.Null(judged["model"]);
        Assert.Empty(all["skipped"]!.AsArray());

        Assert.Equal("pass", (string?)deterministic["status"]);
        Assert.DoesNotContain(deterministic["results"]!.AsArray(), result => (string?)result!["check"] == "calm-voice");
        Assert.Equal(["calm-voice"], deterministic["skipped"]!.AsArray().Select(name => (string?)name));
    }

    // A draft of an update names its entry, so a check that asks about changes sees only the fields that differ.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_draft_of_an_update_is_checked_for_what_it_changes(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        await ReferenceChecksAsync(agent);
        var created = await agent.CallAsync("create_entry", new()
        {
            ["content_type"] = "product-page",
            ["fields"] = new JsonObject { ["headline"] = "A kettle", ["price"] = 199 },
            ["session_id"] = "launch",
            ["rationale"] = "Launch the kettle.",
            ["sources"] = new JsonArray("exports/catalog-2026-10-18.csv"),
        });
        var id = (string?)created["entry"]!["id"];

        var headline = await agent.CallAsync("check_draft", new()
        {
            ["content_type"] = "product-page",
            ["id"] = id,
            ["fields"] = new JsonObject { ["headline"] = "A steel kettle", ["price"] = 199 },
        });
        var price = await agent.CallAsync("check_draft", new()
        {
            ["content_type"] = "product-page",
            ["id"] = id,
            ["fields"] = new JsonObject { ["headline"] = "A kettle", ["price"] = 179 },
        });
        var wrongType = await agent.FailAsync("check_draft", new() { ["content_type"] = "blog-post", ["id"] = id, ["fields"] = new JsonObject() });

        Assert.Equal("pass", (string?)headline["status"]);
        Assert.Equal("fail", (string?)price["status"]);
        Assert.Equal("sourced-prices", (string?)price["results"]!.AsArray().Single(result => (string?)result!["status"] == "fail")!["check"]);
        Assert.Equal(SchemaErrorCodes.ContentTypeNotFound, (string?)wrongType["errors"]![0]!["code"]);
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
        Assert.Equal("fail", (string?)checkedDraft["status"]);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_unknown_content_type_or_selection_is_refused_with_what_to_change(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);

        var unknown = await agent.FailAsync("check_draft", new() { ["content_type"] = "landing-page", ["fields"] = new JsonObject() });
        await agent.CreateAsPersonAsync("landing-page");
        var unknownEntry = await agent.FailAsync("check_draft", new()
        {
            ["content_type"] = "landing-page",
            ["fields"] = new JsonObject(),
            ["id"] = Guid.NewGuid().ToString(),
        });
        var selection = await agent.FailAsync("check_draft", new()
        {
            ["content_type"] = "landing-page",
            ["fields"] = new JsonObject(),
            ["checks"] = "some",
            ["draft"] = true,
        });

        Assert.Equal(SchemaErrorCodes.ContentTypeNotFound, (string?)unknown["errors"]![0]!["code"]);
        Assert.Equal("Call list_content_types for the slugs of the site's content types.", (string?)unknown["hint"]);
        Assert.Equal("entry_not_found", (string?)unknownEntry["errors"]![0]!["code"]);
        Assert.Equal("Call list_entries with the content type's slug for the ids of its entries.", (string?)unknownEntry["hint"]);
        Assert.Equal(
            ["argument_unknown", "argument_invalid"],
            selection["errors"]!.AsArray().Select(error => (string?)error!["code"]));
    }

    // A check reads a draft only of a content type the agent may read, as every tool does.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_draft_of_a_content_type_outside_the_agent_s_scope_is_refused(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider, types: ["blog-post"]);
        await ReferenceChecksAsync(agent);

        var refused = await agent.FailAsync("check_draft", new() { ["content_type"] = "product-page", ["fields"] = S2Draft.DeepClone() });

        Assert.NotEqual(SchemaErrorCodes.ContentTypeNotFound, (string?)refused["errors"]![0]!["code"]);
        Assert.Null(refused["hint"]);
    }

    // A check activated when a model was configured, written straight to the store, since without a model its examples cannot pass.
    private static Task ActiveWithoutItsGateAsync(ConnectedAgent agent, string name, CheckDefinition definition)
    {
        return agent.QueryAsync(async context =>
        {
            var id = Guid.CreateVersion7();
            context.Checks.Add(new CheckRow { Id = id, SiteId = Site.Default.Id, Name = name, Version = 1, ActiveVersion = 1 });
            context.CheckVersions.Add(new CheckVersionRow { Id = Guid.CreateVersion7(), CheckId = id, Version = 1, Definition = definition.ToJson() });
            return await context.SaveChangesAsync();
        });
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
