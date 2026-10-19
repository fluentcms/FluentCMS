using System.Net;
using System.Text;
using System.Text.Json;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Results;
using FluentCMS.Core.Rules;
using FluentCMS.Core.Security;
using FluentCMS.Infrastructure.Models;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.IntegrationTests.ChangeSets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.IntegrationTests.ChangeSets.ChangeSetScenario;

namespace FluentCMS.IntegrationTests.Rules;

// RUL-04, MDL-03: saving a rule proposes checks for it, which its author confirms, edits or rejects; a confirmed check guards content
// only while the rule is active at the version it was proposed for.
public sealed class RuleCheckTests
{
    private const string Slug = "no-superlatives";

    private static readonly RuleDefinition NoSuperlatives = new(
        RuleKind.VoiceAndStyle,
        "Never claim best in class.",
        CheckSeverity.Block,
        new RuleScope(["product-page"], ["headline"]));

    private static readonly Dictionary<string, string?> Model = new()
    {
        ["Models:Profiles:rules:Provider"] = "openai",
        ["Models:Profiles:rules:Model"] = "gpt-fake",
        ["Models:Profiles:rules:ApiKey"] = "sk-test",
        ["Models:Profiles:rules:InputCostPerMillionTokens"] = "1",
        ["Models:Profiles:rules:OutputCostPerMillionTokens"] = "4",
        ["Outbound:AllowedHosts:0"] = "api.openai.com",
    };

    private const string Terms = """
        {"type": "terms", "description": "Product copy never claims best in class.", "settings": "{\"terms\": [\"best in class\"]}",
         "examples": [
           {"name": "plain claim", "expect": "pass", "contentType": "product-page", "fields": "{\"headline\": \"A kettle that boils in two minutes\"}"},
           {"name": "superlative", "expect": "fail", "contentType": "product-page", "fields": "{\"headline\": \"The best in class kettle\"}"}]}
        """;

    private const string Pattern = """
        {"type": "pattern", "description": "Product copy never says best-in-class in any spelling.", "settings": "{\"pattern\": \"best.in.class\", \"ignoreCase\": true}",
         "examples": [
           {"name": "plain claim", "expect": "pass", "contentType": "product-page", "fields": "{\"headline\": \"A kettle that boils in two minutes\"}"},
           {"name": "hyphenated", "expect": "fail", "contentType": "product-page", "fields": "{\"headline\": \"Best-in-class kettle\"}"}]}
        """;

    private static readonly string TwoChecks = $$"""{"checks": [{{Terms}}, {{Pattern}}]}""";

    private static readonly CheckSubject Superlative = new(JsonDocument.Parse("""{"headline": "The best in class kettle"}""").RootElement.Clone(), [], "product-page");

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Without_a_model_a_saved_rule_is_advisory_and_no_compile_is_queued(string provider)
    {
        await using var scenario = await ScenarioAsync(provider, model: null);

        var saved = await SavedAsync(scenario);
        var compilation = (await RuleChecksAsync(scenario, rules => rules.GetAsync(Slug, 1))).Value;

        Assert.True(saved.Rule.IsSuccess);
        Assert.Equal((RuleCompileStatus.Advisory, RuleCompiler.NoModel, (Guid?)null), (compilation.Status, compilation.Message, compilation.JobId));
        Assert.Empty(compilation.Proposals);
        Assert.False(await scenario.QueryAsync(context => context.Jobs.AnyAsync(row => row.Type == RuleCompiler.JobType)));
    }

    // The compile job runs as the check engine for the person who saved the rule; the rule reaches the model only inside a data
    // section of the prompt (SEC-03, RUL-07), and the call and its prompt are recorded (MDL-04).
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task The_compile_proposes_checks_as_the_check_engine_with_the_rule_only_in_the_prompts_data(string provider)
    {
        var model = new CompilingProvider(TwoChecks);
        await using var scenario = await ScenarioAsync(provider, model);
        var saved = await SavedAsync(scenario);

        var compiling = (await RuleChecksAsync(scenario, rules => rules.GetAsync(Slug, 1))).Value;
        var job = await scenario.QueryAsync(context => context.Jobs.AsNoTracking().SingleAsync(row => row.Type == RuleCompiler.JobType));
        var ran = await RunCompileAsync(scenario, compiling.JobId!.Value);
        var compiled = (await RuleChecksAsync(scenario, rules => rules.GetAsync(Slug, 1))).Value;

        Assert.Equal((RuleCompileStatus.Compiling, (string?)null, (Guid?)job.Id), (compiling.Status, compiling.Message, compiling.JobId));
        Assert.Equal(("user", Amir.UserId, "rule", saved.Rule.Value.Id), (job.InitiatorKind, job.InitiatorId!.Value, job.SubjectType, job.SubjectId!.Value));
        Assert.True(ran.IsSuccess, string.Join("; ", ran.Errors.Select(error => error.Message)));
        Assert.Equal((RuleCompileStatus.Proposed, (string?)null), (compiled.Status, compiled.Message));
        Assert.Equal(
            [(1, 1, "no-superlatives-1", "terms", RuleProposalState.Proposed), (2, 1, "no-superlatives-2", "pattern", RuleProposalState.Proposed)],
            compiled.Proposals.Select(proposal => (proposal.Number, proposal.RuleVersion, proposal.CheckName, proposal.Definition.Type, proposal.State)));
        Assert.Equal(["product-page"], compiled.Proposals[0].Definition.Scope.ContentTypes!);

        var call = await scenario.QueryAsync(context => context.ModelCalls.AsNoTracking().SingleAsync());
        Assert.Equal(("rules", "rule:compile"), (call.Profile, call.Purpose));
        var messages = JsonSerializer.Deserialize<JsonElement>(call.Prompt!).EnumerateArray().Select(message => message.GetProperty("text").GetString()!).ToList();
        Assert.DoesNotContain("Never claim best in class.", messages[0], StringComparison.Ordinal);
        Assert.Matches(@"<<<data-[0-9a-f]{16} rule>>>\nNever claim best in class.\n<<<data-[0-9a-f]{16} end>>>", messages[1]);

        var compiledEvent = await scenario.QueryAsync(context => context.AuditEvents.AsNoTracking().SingleAsync(row => row.Operation == "rule.compiled"));
        Assert.Equal(
            ("system", "checks", saved.Rule.Value.Id, call.PromptHash),
            (compiledEvent.InitiatorKind, compiledEvent.InitiatorName, compiledEvent.SubjectId!.Value, compiledEvent.PromptHash));
        Assert.NotNull(compiledEvent.Model);
    }

    // UX-01, S2: the author confirms the proposed terms check; it guards content once the set publishes the rule, and stops once the
    // rule is retired.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_confirmed_check_guards_content_only_while_its_rule_is_active(string provider)
    {
        await using var scenario = await ScenarioAsync(provider, new CompilingProvider(TwoChecks));
        var saved = await SavedAsync(scenario);
        await CompiledAsync(scenario, 1);

        var confirmed = await RuleChecksAsync(scenario, rules => rules.ConfirmAsync(Slug, 1));

        Assert.True(confirmed.IsSuccess, string.Join("; ", confirmed.Errors.Select(error => error.Message)));
        Assert.Equal(("no-superlatives-1", RuleProposalState.Confirmed), (confirmed.Value.CheckName, confirmed.Value.State));
        var check = (await ChecksAsync(scenario, checks => checks.GetAsync("no-superlatives-1"))).Value;
        Assert.Equal((1, (int?)1, "terms"), (check.Version, check.ActiveVersion, check.Definition.Type));
        var events = await scenario.QueryAsync(context => context.AuditEvents.AsNoTracking()
            .Where(row => row.Operation == "check.created" || row.Operation == "check.activated" || row.Operation == "rule.check_confirmed")
            .OrderBy(row => row.Id)
            .ToListAsync());
        Assert.Equal(
            [("check.created", check.Id), ("check.activated", check.Id), ("rule.check_confirmed", saved.Rule.Value.Id)],
            events.Select(row => (row.Operation, row.SubjectId!.Value)));
        Assert.All(events, row => Assert.Equal(("user", "Amir"), (row.InitiatorKind, row.InitiatorName)));

        Assert.DoesNotContain("no-superlatives-1", await ActiveChecksAsync(scenario));
        Assert.Empty((await RunChecksAsync(scenario)).Results);

        await scenario.PublishAsync(saved.ChangeSetId);

        Assert.Contains("no-superlatives-1", await ActiveChecksAsync(scenario));
        var failed = Assert.Single((await RunChecksAsync(scenario)).Results);
        Assert.Equal(("no-superlatives-1", CheckStatus.Fail), (failed.Check, failed.Status));

        var retiring = await scenario.OpenAsync();
        Assert.True((await RulesAsync(scenario, rules => rules.RetireAsync(Slug, ChangeSetTarget.Existing(retiring.Id)))).IsSuccess);
        await scenario.PublishAsync(retiring.Id);

        Assert.DoesNotContain("no-superlatives-1", await ActiveChecksAsync(scenario));
    }

    // The checks confirmed for a revision take over once its set publishes it; until then the active version's checks guard content.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_revisions_checks_take_over_when_the_revision_is_published(string provider)
    {
        await using var scenario = await ScenarioAsync(provider, new CompilingProvider(TwoChecks));
        var saved = await SavedAsync(scenario);
        await CompiledAsync(scenario, 1);
        Assert.True((await RuleChecksAsync(scenario, rules => rules.ConfirmAsync(Slug, 1))).IsSuccess);
        await scenario.PublishAsync(saved.ChangeSetId);

        var revising = await scenario.OpenAsync();
        var revised = new RuleDefinition(RuleKind.VoiceAndStyle, "Never claim best in class, in any spelling.", CheckSeverity.Block, NoSuperlatives.Scope);
        Assert.True((await RulesAsync(scenario, rules => rules.ReviseAsync(Slug, 1, revised, ChangeSetTarget.Existing(revising.Id)))).IsSuccess);
        var compiled = await CompiledAsync(scenario, 2);
        Assert.Equal([3, 4], compiled.Proposals.Select(proposal => proposal.Number));
        Assert.True((await RuleChecksAsync(scenario, rules => rules.ConfirmAsync(Slug, 4))).IsSuccess);

        Assert.Equal(["no-superlatives-1"], await ActiveChecksAsync(scenario));

        await scenario.PublishAsync(revising.Id);

        Assert.Equal(["no-superlatives-4"], await ActiveChecksAsync(scenario));
    }

    // The author can edit a proposal as they confirm it; a confirmation whose examples do not get their verdicts stores nothing.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_confirmation_can_edit_the_check_and_one_whose_examples_fail_stores_nothing(string provider)
    {
        await using var scenario = await ScenarioAsync(provider, new CompilingProvider(TwoChecks));
        await SavedAsync(scenario);
        var proposals = (await CompiledAsync(scenario, 1)).Proposals;
        var widened = Edited(proposals[0].Definition, """{"terms": ["best in class", "world-class"]}""", "A world-class kettle");
        var wrong = Edited(proposals[1].Definition, proposals[1].Definition.Settings.GetRawText(), "A calm kettle");

        var refused = await RuleChecksAsync(scenario, rules => rules.ConfirmAsync(Slug, 2, wrong));
        var edited = await RuleChecksAsync(scenario, rules => rules.ConfirmAsync(Slug, 1, widened, "superlatives"));
        var taken = await RuleChecksAsync(scenario, rules => rules.ConfirmAsync(Slug, 2, checkName: "superlatives"));

        Assert.Equal(CheckErrorCodes.ExampleFailed, Assert.Single(refused.Errors).Code);
        Assert.Equal(CheckErrorCodes.NameTaken, Assert.Single(taken.Errors).Code);
        Assert.Equal(("superlatives", RuleProposalState.Confirmed), (edited.Value.CheckName, edited.Value.State));
        var check = (await ChecksAsync(scenario, checks => checks.GetAsync("superlatives"))).Value;
        Assert.Equal(["best in class", "world-class"], check.Definition.Settings.GetProperty("terms").EnumerateArray().Select(term => term.GetString()));
        Assert.Equal(["superlatives"], (await ChecksAsync(scenario, checks => checks.ListAsync())).Select(summary => summary.Name));

        var after = (await RuleChecksAsync(scenario, rules => rules.GetAsync(Slug, 1))).Value.Proposals;
        Assert.Equal([RuleProposalState.Confirmed, RuleProposalState.Proposed], after.Select(proposal => proposal.State));
        Assert.Equal(
            ["best in class"],
            after[0].Definition.Settings.GetProperty("terms").EnumerateArray().Select(term => term.GetString()));
        Assert.Single(await scenario.QueryAsync(context => context.AuditEvents.AsNoTracking().Where(row => row.Operation == "rule.check_confirmed").ToListAsync()));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_decided_proposal_cannot_be_decided_again(string provider)
    {
        await using var scenario = await ScenarioAsync(provider, new CompilingProvider(TwoChecks));
        await SavedAsync(scenario);
        await CompiledAsync(scenario, 1);

        var confirmed = await RuleChecksAsync(scenario, rules => rules.ConfirmAsync(Slug, 1));
        var rejected = await RuleChecksAsync(scenario, rules => rules.RejectAsync(Slug, 2));
        var again = new List<Result<RuleCheckProposal>>
        {
            await RuleChecksAsync(scenario, rules => rules.ConfirmAsync(Slug, 1)),
            await RuleChecksAsync(scenario, rules => rules.RejectAsync(Slug, 1)),
            await RuleChecksAsync(scenario, rules => rules.ConfirmAsync(Slug, 2)),
            await RuleChecksAsync(scenario, rules => rules.RejectAsync(Slug, 2)),
        };
        var missing = await RuleChecksAsync(scenario, rules => rules.ConfirmAsync(Slug, 9));

        Assert.True(confirmed.IsSuccess);
        Assert.Equal(RuleProposalState.Rejected, rejected.Value.State);
        Assert.All(again, decided => Assert.Equal(RuleErrorCodes.ProposalDecided, Assert.Single(decided.Errors).Code));
        Assert.Equal(RuleErrorCodes.ProposalNotFound, Assert.Single(missing.Errors).Code);
        Assert.Equal(
            ["rule.check_confirmed", "rule.check_rejected"],
            (await scenario.QueryAsync(context => context.AuditEvents.AsNoTracking().Where(row => row.Operation.StartsWith("rule.check_")).OrderBy(row => row.Id).ToListAsync()))
                .Select(row => row.Operation));
    }

    // Proposing again, such as after rejecting what the model proposed, replaces the undecided proposals; a compile it replaced records
    // nothing when it ends.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Proposing_again_replaces_the_undecided_proposals_and_the_replaced_compile_records_nothing(string provider)
    {
        await using var scenario = await ScenarioAsync(provider, new CompilingProvider(TwoChecks));
        var saved = await SavedAsync(scenario);
        var first = (await RuleChecksAsync(scenario, rules => rules.GetAsync(Slug, 1))).Value.JobId!.Value;

        var again = (await RuleChecksAsync(scenario, rules => rules.CompileAsync(Slug, 1))).Value;
        var replaced = await RunCompileAsync(scenario, first);
        var stillCompiling = (await RuleChecksAsync(scenario, rules => rules.GetAsync(Slug, 1))).Value;

        Assert.Equal(RuleCompileStatus.Compiling, again.Status);
        Assert.NotEqual(first, again.JobId);
        Assert.True(replaced.IsSuccess);
        Assert.Equal((RuleCompileStatus.Compiling, again.JobId), (stillCompiling.Status, stillCompiling.JobId));
        Assert.Empty(stillCompiling.Proposals);

        Assert.True((await RunCompileAsync(scenario, again.JobId!.Value)).IsSuccess);
        Assert.True((await RuleChecksAsync(scenario, rules => rules.ConfirmAsync(Slug, 1))).IsSuccess);
        var third = (await RuleChecksAsync(scenario, rules => rules.CompileAsync(Slug, 1))).Value;
        Assert.Equal([(1, RuleProposalState.Confirmed), (2, RuleProposalState.Replaced)], third.Proposals.Select(proposal => (proposal.Number, proposal.State)));
        Assert.Equal(RuleErrorCodes.ProposalDecided, Assert.Single((await RuleChecksAsync(scenario, rules => rules.ConfirmAsync(Slug, 2))).Errors).Code);
        Assert.True((await RunCompileAsync(scenario, third.JobId!.Value)).IsSuccess);

        var compiled = (await RuleChecksAsync(scenario, rules => rules.GetAsync(Slug, 1))).Value;
        Assert.Equal(
            [(1, RuleProposalState.Confirmed), (2, RuleProposalState.Replaced), (3, RuleProposalState.Proposed), (4, RuleProposalState.Proposed)],
            compiled.Proposals.Select(proposal => (proposal.Number, proposal.State)));
        scenario.Initiator = SystemInitiator.Checks;
        var late = await RuleChecksAsync(scenario, rules => rules.RecordAsync(saved.Rule.Value.Id, 1, first, RuleCompileOutcome.Advisory("late")));
        Assert.Equal(RuleErrorCodes.CompileReplaced, Assert.Single(late.Errors).Code);
        Assert.Equal(2, await scenario.QueryAsync(context => context.AuditEvents.CountAsync(row => row.Operation == "rule.compile_requested")));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_answer_that_cannot_be_read_leaves_the_version_advisory(string provider)
    {
        await using var scenario = await ScenarioAsync(provider, new CompilingProvider("I would suggest a terms check."));
        await SavedAsync(scenario);

        var compiled = await CompiledAsync(scenario, 1);

        Assert.Equal(
            (RuleCompileStatus.Advisory, "The model's answer could not be read, so the rule is advisory until checks are proposed again."),
            (compiled.Status, compiled.Message));
        Assert.Empty(compiled.Proposals);
    }

    // D17: only a person decides which checks guard content; only the check engine records what a compile proposed.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Only_a_person_decides_and_only_the_check_engine_records(string provider)
    {
        await using var scenario = await ScenarioAsync(provider, new CompilingProvider(TwoChecks));
        var saved = await SavedAsync(scenario);
        var jobId = (await RuleChecksAsync(scenario, rules => rules.GetAsync(Slug, 1))).Value.JobId!.Value;
        await CompiledAsync(scenario, 1);
        var refusals = new List<IReadOnlyList<Error>>();
        var recordings = new List<IReadOnlyList<Error>>();

        foreach (var initiator in new Initiator[] { Agent, SystemInitiator.Checks })
        {
            scenario.Initiator = initiator;
            refusals.Add((await RuleChecksAsync(scenario, rules => rules.ConfirmAsync(Slug, 1))).Errors);
            refusals.Add((await RuleChecksAsync(scenario, rules => rules.RejectAsync(Slug, 1))).Errors);
            refusals.Add((await RuleChecksAsync(scenario, rules => rules.CompileAsync(Slug, 1))).Errors);
        }

        foreach (var initiator in new Initiator[] { Amir, Agent })
        {
            scenario.Initiator = initiator;
            recordings.Add((await RuleChecksAsync(scenario, rules => rules.CompilingAsync(saved.Rule.Value.Id, 1, jobId))).Errors);
            recordings.Add((await RuleChecksAsync(scenario, rules => rules.RecordAsync(saved.Rule.Value.Id, 1, jobId, RuleCompileOutcome.Advisory("made up")))).Errors);
        }

        Assert.All(refusals, errors => Assert.Equal(RuleErrorCodes.PeopleOnly, Assert.Single(errors).Code));
        Assert.All(recordings, errors => Assert.Equal(RuleErrorCodes.EngineOnly, Assert.Single(errors).Code));
        scenario.Initiator = Amir;
        Assert.All((await RuleChecksAsync(scenario, rules => rules.GetAsync(Slug, 1))).Value.Proposals, proposal => Assert.Equal(RuleProposalState.Proposed, proposal.State));
        Assert.Empty(await ChecksAsync(scenario, checks => checks.ListAsync()));
    }

    // The handlers are resolved together, as the worker does, and the webhook handler among them needs the host's secret protector.
    private static async Task<ChangeSetScenario> ScenarioAsync(string provider, CompilingProvider? model)
    {
        var scenario = await ChangeSetScenario.CreateAsync(provider, model is null ? null : Model, services =>
        {
            services.AddSingleton<ISecretProtector, NoSecrets>();
            if (model is not null)
            {
                services.AddSingleton(new ModelTransport(() => model));
            }
        });
        scenario.Initiator = Amir;
        await scenario.ContentTypeAsync("product-page");
        return scenario;
    }

    private static async Task<(Result<Rule> Rule, Guid ChangeSetId)> SavedAsync(ChangeSetScenario scenario)
    {
        var changeSet = await scenario.OpenAsync();
        var created = await RulesAsync(scenario, rules => rules.CreateAsync(new NewRule(Slug, NoSuperlatives), ChangeSetTarget.Existing(changeSet.Id)));
        Assert.True(created.IsSuccess, string.Join("; ", created.Errors.Select(error => error.Message)));
        return (created, changeSet.Id);
    }

    // Runs the version's compile job and reads what it recorded.
    private static async Task<RuleCompilation> CompiledAsync(ChangeSetScenario scenario, int version)
    {
        var jobId = (await RuleChecksAsync(scenario, rules => rules.GetAsync(Slug, version))).Value.JobId!.Value;
        var ran = await RunCompileAsync(scenario, jobId);
        Assert.True(ran.IsSuccess, string.Join("; ", ran.Errors.Select(error => error.Message)));
        return (await RuleChecksAsync(scenario, rules => rules.GetAsync(Slug, version))).Value;
    }

    // As the job worker runs it: the handler as the engine its type is listed for, with the job as stored.
    private static async Task<Result> RunCompileAsync(ChangeSetScenario scenario, Guid jobId)
    {
        var job = await RunAsync(scenario, services => services.GetRequiredService<IJobQueue>().GetAsync(jobId));
        var person = scenario.Initiator;
        scenario.Initiator = SystemInitiator.Checks;
        try
        {
            return await RunAsync(scenario, services => services.GetServices<IJobHandler>()
                .Single(handler => handler.Type == RuleCompiler.JobType)
                .RunAsync(new JobContext(job.Value, services, (_, _) => Task.FromResult(true)), CancellationToken.None));
        }
        finally
        {
            scenario.Initiator = person;
        }
    }

    private static Task<List<string>> ActiveChecksAsync(ChangeSetScenario scenario)
    {
        return RunAsync(scenario, async services => (await services.GetRequiredService<ICheckDirectory>().ListActiveAsync()).Select(check => check.Name).ToList());
    }

    private static Task<CheckRun> RunChecksAsync(ChangeSetScenario scenario)
    {
        return RunAsync(scenario, services => services.GetRequiredService<ICheckRunner>().RunAsync(Superlative));
    }

    // The proposal with other settings and its failing example replaced by one with the headline given.
    private static CheckDefinition Edited(CheckDefinition proposed, string settings, string failing)
    {
        return new CheckDefinition(
            proposed.Type,
            proposed.Description,
            JsonDocument.Parse(settings).RootElement.Clone(),
            [
                proposed.Examples.First(example => example.Expected == CheckVerdict.Pass),
                new CheckExample("edited", CheckVerdict.Fail, JsonDocument.Parse(JsonSerializer.Serialize(new { headline = failing })).RootElement.Clone(), contentType: "product-page"),
            ],
            proposed.Severity,
            proposed.Scope);
    }

    private static Task<T> RulesAsync<T>(ChangeSetScenario scenario, Func<IRuleDirectory, Task<T>> action)
    {
        return RunAsync(scenario, services => action(services.GetRequiredService<IRuleDirectory>()));
    }

    private static Task<T> RuleChecksAsync<T>(ChangeSetScenario scenario, Func<IRuleChecks, Task<T>> action)
    {
        return RunAsync(scenario, services => action(services.GetRequiredService<IRuleChecks>()));
    }

    private static Task<T> ChecksAsync<T>(ChangeSetScenario scenario, Func<ICheckDirectory, Task<T>> action)
    {
        return RunAsync(scenario, services => action(services.GetRequiredService<ICheckDirectory>()));
    }

    // In a scope of its own, as whoever Initiator names.
    private static async Task<T> RunAsync<T>(ChangeSetScenario scenario, Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private sealed class NoSecrets : ISecretProtector
    {
        public string Protect(string purpose, string secret) => throw new NotSupportedException("Rule tests protect no secrets.");

        public string Unprotect(string purpose, string protectedSecret) => throw new NotSupportedException("Rule tests protect no secrets.");
    }

    // Answers like the OpenAI chat API, with the same answer to every request.
    private sealed class CompilingProvider(string answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = JsonSerializer.Serialize(new
            {
                id = "chatcmpl-1",
                @object = "chat.completion",
                created = 1760000000,
                model = "gpt-fake-2026-01-01",
                choices = new[] { new { index = 0, message = new { role = "assistant", content = answer }, finish_reason = "stop" } },
                usage = new { prompt_tokens = 900, completion_tokens = 300, total_tokens = 1200 },
            });

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
