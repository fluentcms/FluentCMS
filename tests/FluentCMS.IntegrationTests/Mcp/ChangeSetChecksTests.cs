using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Content;
using FluentCMS.Core.Extensions;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Results;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Mcp;

// CHK-03: checks run on every change set before routing, and their results, evidence and cost are stored with the set.
public sealed class ChangeSetChecksTests
{
    private static readonly Dictionary<string, string?> FastQueue = new() { ["JobQueue:PollingInterval"] = "00:00:00.050" };

    // S2: a set with "best in class", an image without alt text and copy that cites no source fails three checks that say what to change;
    // the fixed content passes. The checks, not the agent, move the set.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_small_set_is_checked_when_it_is_submitted_and_the_check_engine_moves_it(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        await ProductChecksAsync(agent);

        var failing = await SubmitAsync(agent, "s2", "The best in class kettle ![](/media/kettle.jpg)");
        var passing = await SubmitAsync(agent, "s2-fixed", "A kettle that boils in two minutes ![A steel kettle](/media/kettle.jpg)", "exports/catalog.csv");

        Assert.Equal("checks_failed", (string?)failing["changeSet"]!["state"]);
        var checks = failing["checks"]!;
        Assert.Equal(("inline", "fail", 1), ((string?)checks["mode"], (string?)checks["status"], (int)checks["number"]!));
        var results = checks["items"]!.AsArray().Single()!["results"]!.AsArray();
        Assert.Equal(["alt-text", "no-superlatives", "sourced-copy"], results.Select(result => (string?)result!["check"]));
        Assert.All(results, result => Assert.Equal("fail", (string?)result!["status"]));
        Assert.Equal("/title", (string?)results[1]!["findings"]![0]!["pointer"]);
        Assert.Null(failing["job"]);

        Assert.Equal("awaiting_review", (string?)passing["changeSet"]!["state"]);
        Assert.Equal("pass", (string?)passing["checks"]!["status"]);

        // The run is stored with the set, and the backup job only waits in case it had not finished.
        var setId = (string)failing["changeSet"]!["id"]!;
        var read = await agent.CallAsync("get_change_set_checks", new() { ["id"] = setId });
        Assert.Equal(1, (int)read["checks"]!["number"]!);
        Assert.Equal("queued", (string?)read["job"]!["state"]);

        var events = await agent.QueryAsync(context => context.AuditEvents.AsNoTracking()
            .Where(row => row.ChangeSetId == Guid.Parse(setId) || row.SubjectId == Guid.Parse(setId))
            .ToListAsync());
        Assert.Equal("agent", events.Single(row => row.Operation == "change_set.submitted").InitiatorKind);
        var byEngine = events.Where(row => row.Operation is "change_set.checked" or "change_set.checks_failed").ToList();
        Assert.Equal(2, byEngine.Count);
        Assert.All(byEngine, row => Assert.Equal(("system", "checks", (Guid?)null), (row.InitiatorKind, row.InitiatorName, row.AgentCredentialId)));
    }

    // A run belongs to its submission: the job of an earlier one, which may still fire, leaves a set reopened or submitted again
    // as it is, and adds no run.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task The_job_of_an_earlier_submission_changes_nothing(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        await ProductChecksAsync(agent);
        var first = await SubmitAsync(agent, "s2", "The best in class kettle");
        var setId = Guid.Parse((string)first["changeSet"]!["id"]!);
        var earlier = (await agent.CallAsync("get_change_set_checks", new() { ["id"] = setId.ToString() }))["job"]!;
        var earlierJob = Guid.Parse((string)earlier["id"]!);

        await agent.CallAsync("reopen_change_set", new() { ["id"] = setId.ToString() });
        var whileOpen = await agent.AsEngineAsync<IChangeSetChecks, Result<ChangeSetCheckRun?>>(checks => checks.RunAsync(setId, earlierJob, CheckRunMode.Job));
        var again = await agent.CallAsync("submit_change_set", new() { ["id"] = setId.ToString() });
        var afterwards = await agent.AsEngineAsync<IChangeSetChecks, Result<ChangeSetCheckRun?>>(checks => checks.RunAsync(setId, earlierJob, CheckRunMode.Job));
        var abandoned = await agent.AsEngineAsync<IChangeSetChecks, Result<ChangeSetCheckRun?>>(checks => checks.AbandonAsync(setId, earlierJob, CheckRunMode.Job));

        Assert.Equal(1, whileOpen.Value!.Number);
        Assert.Equal(2, (int)again["checks"]!["number"]!);
        Assert.Equal((2, 2), (afterwards.Value!.Number, abandoned.Value!.Number));
        var read = await agent.CallAsync("get_change_set_checks", new() { ["id"] = setId.ToString() });
        Assert.Equal(("checks_failed", 2), ((string?)read["changeSet"]!["state"], (int)read["checks"]!["number"]!));
        Assert.NotEqual(earlierJob, Guid.Parse((string)read["job"]!["id"]!));
        Assert.Equal(2, await agent.QueryAsync(context => context.CheckRuns.CountAsync(run => run.ChangeSetId == setId)));
    }

    // An item the engine cannot read, such as an entry deleted after it was proposed, fails the set with what to do about it, and
    // the other items are still checked.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_item_that_cannot_be_read_fails_the_set_with_a_problem(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        await ProductChecksAsync(agent);
        var kept = await CreateAsync(agent, "both", "The best in class kettle");
        var deleted = await CreateAsync(agent, "both", "A kettle");
        var deletedId = Guid.Parse((string)deleted["entry"]!["id"]!);
        Assert.True((await agent.AsPersonAsync<IEntryDirectory>(directory => directory.DeleteAsync(deletedId, 1))).IsSuccess);

        var submitted = await agent.CallAsync("submit_change_set", new() { ["id"] = (string?)kept["changeSet"]!["id"] });

        Assert.Equal("checks_failed", (string?)submitted["changeSet"]!["state"]);
        var checks = submitted["checks"]!;
        Assert.Equal("fail", (string?)checks["status"]);
        Assert.Contains(deletedId.ToString(), (string?)Assert.Single(checks["problems"]!.AsArray()));
        Assert.Equal((string?)kept["entry"]!["id"], (string?)Assert.Single(checks["items"]!.AsArray())!["itemId"]);
    }

    // A set with nothing to check passes and waits for review; routing decides more once it lands (F-RTE).
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_set_no_check_applies_to_waits_for_review(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        await ProductChecksAsync(agent);
        await agent.CreateAsPersonAsync("note");

        var created = await agent.CallAsync("create_entry", new()
        {
            ["content_type"] = "note",
            ["fields"] = new JsonObject { ["title"] = "The best in class note" },
            ["session_id"] = "notes",
            ["rationale"] = "Add a note.",
        });
        var submitted = await agent.CallAsync("submit_change_set", new() { ["id"] = (string?)created["changeSet"]!["id"] });

        Assert.Equal("awaiting_review", (string?)submitted["changeSet"]!["state"]);
        Assert.Equal("pass", (string?)submitted["checks"]!["status"]);
        Assert.Empty(submitted["checks"]!["items"]!.AsArray());
    }

    // LLM-judged checks run as a job with progress (PERF-02), as the check engine, and their model calls count against the agent
    // whose set they judge (SEC-04).
    [Fact]
    public async Task A_set_an_LLM_judged_check_reads_is_checked_as_a_job_charged_to_the_agent()
    {
        await using var agent = await ConnectedAgent.StartAsync(
            "sqlite",
            settings: FastQueue,
            configure: builder => builder.Services.AddFluentCmsExtension<ICheckType, RecordingJudge>());
        await ProductChecksAsync(agent);
        await ActivateAsync(agent, "calm", Definition(RecordingJudge.Name, "{}", "A kettle", "BUY!!!"));

        var submitted = await SubmitAsync(agent, "judged", "BUY THIS KETTLE!!!", "exports/catalog.csv");
        var setId = (string)submitted["changeSet"]!["id"]!;
        var finished = await FinishedAsync(agent, setId);

        Assert.Equal("checks_running", (string?)submitted["changeSet"]!["state"]);
        Assert.Null(submitted["checks"]);
        Assert.Equal("checks_failed", (string?)finished["changeSet"]!["state"]);
        Assert.Equal(("job", "fail"), ((string?)finished["checks"]!["mode"], (string?)finished["checks"]!["status"]));
        Assert.Equal(("succeeded", 1, 1), ((string?)finished["job"]!["state"], (int)finished["job"]!["done"]!, (int)finished["job"]!["total"]!));

        var (chargedTo, ranAs) = RecordingJudge.Calls.Last();
        Assert.Equal(agent.Credential.Id, chargedTo);
        Assert.Equal(SystemInitiator.Checks, ranAs);
    }

    // PERF-02: deterministic checks over a 200-item set finish in under ten seconds; a set that large runs as a job.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Deterministic_checks_on_a_200_item_set_finish_in_under_ten_seconds(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider, settings: FastQueue);
        await ProductChecksAsync(agent);

        string? setId = null;
        for (var item = 1; item <= 200; item++)
        {
            var created = await agent.CallAsync("create_entry", new()
            {
                ["content_type"] = "product-page",
                ["fields"] = new JsonObject { ["title"] = $"Kettle {item}" },
                ["session_id"] = "catalog",
                ["rationale"] = "Import the catalog.",
                ["sources"] = new JsonArray("exports/catalog-2026-10-18.csv"),
            });
            setId = (string?)created["changeSet"]!["id"];
        }

        var submitted = await agent.CallAsync("submit_change_set", new() { ["id"] = setId });
        var finished = await FinishedAsync(agent, setId!);

        Assert.Null(submitted["checks"]);
        var run = finished["checks"]!;
        Assert.Equal(("job", "pass", 200), ((string?)run["mode"], (string?)run["status"], run["items"]!.AsArray().Count));
        var took = DateTimeOffset.Parse((string)run["finishedAt"]!) - DateTimeOffset.Parse((string)run["startedAt"]!);
        Assert.True(took < TimeSpan.FromSeconds(10), $"The run took {took}.");
        Assert.Equal("awaiting_review", (string?)finished["changeSet"]!["state"]);
    }

    // The checks of the reference scenario (S2), activated by a person, on a product page's title: no "best in class", alt text on
    // every image, and a source for the copy.
    private static async Task ProductChecksAsync(ConnectedAgent agent)
    {
        await agent.CreateAsPersonAsync("product-page");
        await ActivateAsync(agent, "no-superlatives", Definition("terms", """{"terms": ["best in class"]}""", "A kettle", "Best in class"));
        await ActivateAsync(agent, "alt-text", Definition("altText", "{}", "![A kettle](/k.jpg)", "![](/k.jpg)"));
        await ActivateAsync(agent, "sourced-copy", Definition("sources", "{}", "A kettle", "A kettle", ["exports/catalog.csv"]));
    }

    private static async Task ActivateAsync(ConnectedAgent agent, string name, CheckDefinition definition)
    {
        await agent.AsPersonAsync<ICheckDirectory, Check>(directory => directory.CreateAsync(new NewCheck(name, definition)));
        await agent.AsPersonAsync<ICheckDirectory, Check>(directory => directory.ActivateAsync(name, 1));
    }

    private static CheckDefinition Definition(string type, string settings, string passing, string failing, IReadOnlyList<string>? passingSources = null)
    {
        return new CheckDefinition(
            type,
            $"The {type} check of the reference scenario.",
            JsonSerializer.Deserialize<JsonElement>(settings),
            [
                new CheckExample("passes", CheckVerdict.Pass, Title(passing), passingSources, "product-page"),
                new CheckExample("fails", CheckVerdict.Fail, Title(failing), contentType: "product-page"),
            ],
            scope: new CheckScope(["product-page"]));
    }

    private static JsonElement Title(string title)
    {
        return JsonSerializer.SerializeToElement(new { title });
    }

    private static async Task<JsonObject> SubmitAsync(ConnectedAgent agent, string session, string title, params string[] sources)
    {
        var created = await CreateAsync(agent, session, title, sources);
        return await agent.CallAsync("submit_change_set", new() { ["id"] = (string?)created["changeSet"]!["id"] });
    }

    private static Task<JsonObject> CreateAsync(ConnectedAgent agent, string session, string title, params string[] sources)
    {
        return agent.CallAsync("create_entry", new()
        {
            ["content_type"] = "product-page",
            ["fields"] = new JsonObject { ["title"] = title },
            ["session_id"] = session,
            ["rationale"] = "Launch the kettle.",
            ["sources"] = sources.Length == 0 ? null : new JsonArray([.. sources.Select(source => JsonValue.Create(source))]),
        });
    }

    private static async Task<JsonObject> FinishedAsync(ConnectedAgent agent, string setId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            var read = await agent.CallAsync("get_change_set_checks", new() { ["id"] = setId });
            if ((string?)read["job"]?["state"] is "succeeded" or "failed" or "dead" || DateTime.UtcNow > deadline)
            {
                return read;
            }

            await Task.Delay(100);
        }
    }

    // An LLM-judged type that asks no model: it fails content with "!!!" and records whom it was charged to and as whom it ran.
    private sealed class RecordingJudge : ICheckType
    {
        public const string Name = "sample.judge";

        public static ConcurrentQueue<(Guid? ChargedTo, Initiator? RanAs)> Calls { get; } = new();

        string ICheckType.Name => Name;

        public CheckKind Kind => CheckKind.LlmJudged;

        public IReadOnlyList<Error> ValidateSettings(JsonElement settings)
        {
            return new CheckSettingsReader(Name, settings).Errors();
        }

        public Task<CheckOutcome> EvaluateAsync(CheckInput input, CancellationToken cancellationToken)
        {
            Calls.Enqueue((input.ChargedTo, input.Services.GetRequiredService<ICurrentInitiator>().Current));
            var loud = input.Subject.Fields.ValueKind == JsonValueKind.Object
                && input.Subject.Fields.EnumerateObject().Any(field => field.Value.ValueKind == JsonValueKind.String && field.Value.GetString()!.Contains("!!!"));

            return Task.FromResult(loud ? CheckOutcome.Fail("Drop the exclamation marks.", "/title") : CheckOutcome.Pass());
        }
    }
}
