using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Content;
using FluentCMS.Core.Extensions;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Results;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Mcp;

// CHK-03: checks run on every change set before routing, and their results, evidence and cost are stored with the set.
public sealed class ChangeSetChecksTests
{
    private static readonly Dictionary<string, string?> FastQueue = new() { ["JobQueue:PollingInterval"] = "00:00:00.050" };

    // The worker polls once as the host starts, so a job enqueued later waits for the test.
    private static readonly Dictionary<string, string?> IdleQueue = new() { ["JobQueue:PollingInterval"] = "01:00:00" };

    // S2: a set with "best in class", an image without alt text and copy that cites no source fails three checks that say what to change.
    // The agent reopens it, updates the entry from the version the set proposes, and the fixed set passes. The checks, not the agent,
    // move the set.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_small_set_is_checked_when_it_is_submitted_fixed_in_place_and_the_check_engine_moves_it(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        await ProductChecksAsync(agent);

        var created = await CreateAsync(agent, "s2", "The best in class kettle ![](/media/kettle.jpg)");
        var failing = await agent.CallAsync("submit_change_set", new() { ["id"] = (string?)created["changeSet"]!["id"] });

        Assert.Equal("checks_failed", (string?)failing["changeSet"]!["state"]);
        var checks = failing["checks"]!;
        Assert.Equal(("inline", "fail", 1), ((string?)checks["mode"], (string?)checks["status"], (int)checks["number"]!));
        var results = checks["items"]!.AsArray().Single()!["results"]!.AsArray();
        Assert.Equal(["alt-text", "no-superlatives", "sourced-copy"], results.Select(result => (string?)result!["check"]));
        Assert.All(results, result => Assert.Equal("fail", (string?)result!["status"]));
        Assert.Equal("/title", (string?)results[1]!["findings"]![0]!["pointer"]);
        Assert.Null(failing["job"]);

        // The run is stored with the set, and the backup job only waits in case it had not finished.
        var setId = (string)failing["changeSet"]!["id"]!;
        var read = await agent.CallAsync("get_change_set_checks", new() { ["id"] = setId });
        Assert.Equal(1, (int)read["checks"]!["number"]!);
        Assert.Equal("queued", (string?)read["job"]!["state"]);

        await agent.CallAsync("reopen_change_set", new() { ["id"] = setId });
        var fixedEntry = await agent.CallAsync("update_entry", new()
        {
            ["id"] = (string?)created["entry"]!["id"],
            ["base_version"] = 1,
            ["fields"] = new JsonObject { ["title"] = "A kettle that boils in two minutes ![A steel kettle](/media/kettle.jpg)" },
            ["session_id"] = "s2",
            ["rationale"] = "Fix what the checks found.",
            ["sources"] = new JsonArray("exports/catalog.csv"),
        });
        var passing = await agent.CallAsync("submit_change_set", new() { ["id"] = setId });

        Assert.Equal(setId, (string?)fixedEntry["changeSet"]!["id"]);
        Assert.Equal(("awaiting_review", "pass", 2), ((string?)passing["changeSet"]!["state"], (string?)passing["checks"]!["status"], (int)passing["checks"]!["number"]!));
        Assert.Equal(2, (int)passing["checks"]!["items"]![0]!["version"]!);
        var stored = await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.GetAsync(Guid.Parse(setId)));
        var operation = Assert.Single(stored.Operations);
        Assert.Equal((OperationKind.Create, (int?)null, (int?)2), (operation.Kind, operation.BaseVersion, operation.ProposedVersion));

        var events = await agent.QueryAsync(context => context.AuditEvents.AsNoTracking()
            .Where(row => row.ChangeSetId == Guid.Parse(setId) || row.SubjectId == Guid.Parse(setId))
            .ToListAsync());
        Assert.All(events.Where(row => row.Operation == "change_set.submitted"), row => Assert.Equal("agent", row.InitiatorKind));
        Assert.Single(events, row => row.Operation == "change_set.operation_updated");
        var byEngine = events.Where(row => row.Operation is "change_set.checked" or "change_set.checks_failed" or "change_set.awaiting_review").ToList();
        Assert.Equal(4, byEngine.Count);
        Assert.All(byEngine, row => Assert.Equal(("system", "checks", (Guid?)null), (row.InitiatorKind, row.InitiatorName, row.AgentCredentialId)));
    }

    // A run belongs to its submission: the job of an earlier one, which may still fire, leaves a set reopened or submitted again
    // as it is, and adds no run, even when a person, who may move any set, runs it.
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
        var whileOpen = await agent.AsPersonAsync<IChangeSetChecks, ChangeSetCheckRun?>(checks => checks.RunAsync(setId, earlierJob, CheckRunMode.Job));
        var again = await agent.CallAsync("submit_change_set", new() { ["id"] = setId.ToString() });
        var afterwards = await agent.AsPersonAsync<IChangeSetChecks, ChangeSetCheckRun?>(checks => checks.RunAsync(setId, earlierJob, CheckRunMode.Job));
        var abandoned = await agent.AsPersonAsync<IChangeSetChecks, ChangeSetCheckRun?>(checks => checks.AbandonAsync(setId, earlierJob, CheckRunMode.Job));

        Assert.Equal(1, whileOpen!.Number);
        Assert.Equal(2, (int)again["checks"]!["number"]!);
        Assert.Equal((2, 2), (afterwards!.Number, abandoned!.Number));
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

    // CHG-06: a revert restores what was published and takes no new operations, so a check that fails it, such as one asking for a
    // source the revert cannot cite, informs its reviewer instead of holding it.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_revert_goes_to_review_with_the_results_of_its_checks(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        await ProductChecksAsync(agent);
        var created = await SubmitAsync(agent, "launch", "A kettle", "exports/catalog.csv");
        await PublishAsync(agent, created);
        var entryId = (string)(await agent.QueryAsync(context => context.Entries.AsNoTracking().Select(row => row.Id.ToString()).SingleAsync()));
        var renamed = await agent.CallAsync("update_entry", new()
        {
            ["id"] = entryId,
            ["base_version"] = 1,
            ["fields"] = new JsonObject { ["title"] = "A steel kettle" },
            ["session_id"] = "rename",
            ["rationale"] = "Name the material.",
            ["sources"] = new JsonArray("exports/catalog.csv"),
        });
        var published = await PublishAsync(agent, await agent.CallAsync("submit_change_set", new() { ["id"] = (string?)renamed["changeSet"]!["id"] }));

        var revert = await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.RevertAsync(published, "The rename was wrong."));
        var submitted = await agent.AsPersonAsync<IChangeSetChecks, ChangeSetSubmission>(checks => checks.SubmitAsync(revert.Id));

        Assert.Equal(ChangeSetState.AwaitingReview, submitted.ChangeSet.State);
        Assert.Equal(CheckStatus.Fail, submitted.Run!.Status);
        var result = Assert.Single(submitted.Run.Items.Single().Results, result => result.Check == "sourced-copy");
        Assert.Equal((CheckStatus.Fail, 1), (result.Status, submitted.Run.Items.Single().Version));
    }

    // A set whose check job ended without moving it, as when the host stopped during the job's last attempt, is failed when the
    // agent submits it again, so it can be reopened rather than stay in checks running.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_set_whose_check_job_ended_without_moving_it_is_failed_when_it_is_submitted_again(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(
            provider,
            settings: IdleQueue,
            configure: builder => builder.Services.AddFluentCmsExtension<ICheckType, RecordingJudge>());
        await ProductChecksAsync(agent);
        await ActivateAsync(agent, "calm", Definition(RecordingJudge.Name, "{}", "A kettle", "BUY!!!"));
        var submitted = await SubmitAsync(agent, "stuck", "A kettle", "exports/catalog.csv");
        var setId = (string)submitted["changeSet"]!["id"]!;
        var jobId = Guid.Parse((string)submitted["job"]!["id"]!);
        await agent.QueryAsync(context => context.Jobs.Where(row => row.Id == jobId).ExecuteUpdateAsync(row => row.SetProperty(job => job.State, JobState.Dead)));

        var again = await agent.CallAsync("submit_change_set", new() { ["id"] = setId });

        Assert.Equal("checks_running", (string?)submitted["changeSet"]!["state"]);
        Assert.Equal("checks_failed", (string?)again["changeSet"]!["state"]);
        Assert.Contains(jobId.ToString(), (string?)Assert.Single(again["checks"]!["problems"]!.AsArray()));
        var reopened = await agent.CallAsync("reopen_change_set", new() { ["id"] = setId });
        Assert.Equal("open", (string?)reopened["changeSet"]!["state"]);

        // A person can fail a stuck set too.
        var other = await SubmitAsync(agent, "stuck-too", "A kettle", "exports/catalog.csv");
        var otherId = Guid.Parse((string)other["changeSet"]!["id"]!);
        var otherJob = Guid.Parse((string)other["job"]!["id"]!);
        await agent.QueryAsync(context => context.Jobs.Where(row => row.Id == otherJob).ExecuteUpdateAsync(row => row.SetProperty(job => job.State, JobState.Failed)));
        var byPerson = await agent.AsPersonAsync<IChangeSetChecks, ChangeSetSubmission>(checks => checks.SubmitAsync(otherId));
        Assert.Equal(ChangeSetState.ChecksFailed, byPerson.ChangeSet.State);
        Assert.Contains(otherJob.ToString(), Assert.Single(byPerson.Run!.Problems));
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
    // whose set they judge (SEC-04). Each judgement names its model and prompt in the audit log (CHK-05).
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_set_is_checked_as_a_job_while_an_LLM_judged_check_is_active_and_charged_to_the_agent(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(
            provider,
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

        var (onBehalfOf, ranAs) = RecordingJudge.Calls.Last();
        Assert.Equal(agent.Credential.Id, Assert.IsType<AgentInitiator>(onBehalfOf).CredentialId);
        Assert.Equal(SystemInitiator.Checks, ranAs);

        var judged = (string?)finished["checks"]!["items"]![0]!["results"]!.AsArray().Single(result => (string?)result!["check"] == "calm")!["model"]!["model"];
        Assert.Equal((RecordingJudge.Model, RecordingJudge.Cost), (judged, (decimal)finished["checks"]!["cost"]!));
        var events = await agent.QueryAsync(context => context.AuditEvents.AsNoTracking().Where(row => row.Operation == "check.judged").ToListAsync());
        var judgement = Assert.Single(events);
        Assert.Equal(
            (Guid.Parse(setId), RecordingJudge.Model, RecordingJudge.PromptHash, "system", "checks"),
            (judgement.ChangeSetId!.Value, judgement.Model, judgement.PromptHash, judgement.InitiatorKind, judgement.InitiatorName));
    }

    // SEC-04: the engine judges a person's set on that person's behalf, so its LLM-judged checks run, uncapped as people are, rather
    // than being refused for naming no one.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_person_s_set_is_judged_on_that_person_s_behalf(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(
            provider,
            settings: FastQueue,
            configure: builder => builder.Services.AddFluentCmsExtension<ICheckType, RecordingJudge>());
        await ProductChecksAsync(agent);
        await ActivateAsync(agent, "calm", Definition(RecordingJudge.Name, "{}", "A kettle", "BUY!!!"));

        var set = await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.OpenAsync(new NewChangeSet("Launch the kettle.", ["exports/catalog.csv"])));
        var entry = await agent.AsPersonAsync<IEntryDirectory, Entry>(directory => directory.CreateAsync(new NewEntry("product-page", Title("BUY THIS KETTLE!!!"))));
        await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory =>
            directory.AddOperationAsync(set.Id, ChangeOperation.Create(OperationKind.Create, ItemKind.Entry, entry.Id, null, 1).Value));
        await agent.AsPersonAsync<IChangeSetChecks, ChangeSetSubmission>(checks => checks.SubmitAsync(set.Id));

        var deadline = DateTime.UtcNow.AddSeconds(60);
        ChangeSetChecksState state;
        do
        {
            await Task.Delay(100);
            state = await agent.AsPersonAsync<IChangeSetChecks, ChangeSetChecksState>(checks => checks.GetAsync(set.Id));
        }
        while (state.Job?.State is not (JobState.Succeeded or JobState.Failed or JobState.Dead) && DateTime.UtcNow < deadline);

        Assert.Equal(ChangeSetState.ChecksFailed, state.ChangeSet.State);
        Assert.Contains(RecordingJudge.Calls, call => call.OnBehalfOf is UserInitiator && call.RanAs == SystemInitiator.Checks);
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

    // A person approves and publishes a set that passed its checks.
    private static async Task<Guid> PublishAsync(ConnectedAgent agent, JsonObject submitted)
    {
        Assert.Equal("awaiting_review", (string?)submitted["changeSet"]!["state"]);
        var id = Guid.Parse((string)submitted["changeSet"]!["id"]!);
        await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.TransitionAsync(id, ChangeSetState.Approved));
        await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.TransitionAsync(id, ChangeSetState.Published));
        return id;
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

    // An LLM-judged type that asks no model: it fails content with "!!!", names a made-up model call, and records whom it was
    // charged to and as whom it ran.
    private sealed class RecordingJudge : ICheckType
    {
        public const string Name = "sample.judge";

        public const string Model = "judge-1";

        public const string PromptHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        public const decimal Cost = 0.25m;

        public static ConcurrentQueue<(Initiator? OnBehalfOf, Initiator? RanAs)> Calls { get; } = new();

        string ICheckType.Name => Name;

        public CheckKind Kind => CheckKind.LlmJudged;

        public IReadOnlyList<Error> ValidateSettings(JsonElement settings)
        {
            return new CheckSettingsReader(Name, settings).Errors();
        }

        public Task<CheckOutcome> EvaluateAsync(CheckInput input, CancellationToken cancellationToken)
        {
            Calls.Enqueue((input.OnBehalfOf, input.Services.GetRequiredService<ICurrentInitiator>().Current));
            var loud = input.Subject.Fields.ValueKind == JsonValueKind.Object
                && input.Subject.Fields.EnumerateObject().Any(field => field.Value.ValueKind == JsonValueKind.String && field.Value.GetString()!.Contains("!!!"));

            var outcome = loud ? CheckOutcome.Fail("Drop the exclamation marks.", "/title") : CheckOutcome.Pass();
            return Task.FromResult(outcome.WithModel(new CheckModelUse(Guid.CreateVersion7(), "openai", Model, PromptHash, Cost)));
        }
    }
}
