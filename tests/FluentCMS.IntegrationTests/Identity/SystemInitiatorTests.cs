using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Agents;
using FluentCMS.Core.Audit;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Extensions;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Models;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.IntegrationTests.Audit;
using FluentCMS.IntegrationTests.Checks;
using FluentCMS.IntegrationTests.Models;
using FluentCMS.IntegrationTests.Mcp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Identity;

// ADR-0012: what the product does on its own is recorded as the system, never as a person or an agent.
public sealed class SystemInitiatorTests
{
    private static readonly AuditSubject Set = new("change_set", Guid.NewGuid());

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_engine_s_event_reads_back_and_exports_as_the_system_with_no_credential(string provider)
    {
        await using var scenario = await AuditScenario.CreateAsync(provider);
        var at = new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

        await scenario.AppendAsync(at, new NewAuditEvent("change_set.checks_passed") { SubjectType = Set.Type, SubjectId = Set.Id }, SystemInitiator.Checks);

        var read = Assert.Single(await scenario.ReadAsync(new AuditQuery { Subject = Set }));
        var exported = JsonNode.Parse(await scenario.ExportAsync(new AuditQuery { Subject = Set }, AuditExportFormat.Json))!.AsArray();
        var row = await QueryAsync(scenario, context => context.AuditEvents.AsNoTracking().SingleAsync(e => e.Operation == "change_set.checks_passed"));

        var system = Assert.IsType<SystemInitiator>(read.Initiator);
        Assert.Equal(("checks", SystemInitiator.Checks.Id), (system.Component, system.Id));
        Assert.Null(read.AgentCredentialId);
        Assert.Equal(("system", SystemInitiator.Checks.Id, "checks", (Guid?)null), (row.InitiatorKind, row.InitiatorId, row.InitiatorName, row.AgentCredentialId));
        var exportedEvent = exported.Single(e => (string?)e!["operation"] == "change_set.checks_passed")!;
        Assert.Equal(
            ("system", SystemInitiator.Checks.Id.ToString(), "checks", (string?)null),
            ((string?)exportedEvent["initiatorKind"], (string?)exportedEvent["initiatorId"], (string?)exportedEvent["initiatorName"], (string?)exportedEvent["agentCredentialId"]));
    }

    // Only a person issues credentials and activates checks; an engine is refused, as an agent is.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_engine_cannot_write_or_activate_a_check_issue_a_credential_or_manage_users(string provider)
    {
        await using var scenario = await CheckScenario.CreateAsync(provider);
        await scenario.CreateAsync("no-superlatives", CheckScenario.NoSuperlatives());
        scenario.Initiator = SystemInitiator.Checks;

        var create = await scenario.CreateAsync("more-checks", CheckScenario.NoSuperlatives());
        var revise = await scenario.RunAsync(directory => directory.ReviseAsync("no-superlatives", 1, CheckScenario.NoSuperlatives("finest")));
        var activate = await scenario.RunAsync(directory => directory.ActivateAsync("no-superlatives", 1));
        await using var users = scenario.Services.CreateAsyncScope();
        var user = await users.ServiceProvider.GetRequiredService<IUserDirectory>().CreateAsync(new NewUser("engine@example.com", "Engine", "a-long-enough-password-1"));
        await using var scope = scenario.Services.CreateAsyncScope();
        var issue = await scope.ServiceProvider.GetRequiredService<IAgentCredentialDirectory>().IssueAsync(new NewAgentCredential(
            "engine-made",
            DateTimeOffset.UtcNow.AddDays(30),
            AgentScope.Create([Site.Default.Id], null, null, [AgentOperation.Read]).Value,
            AgentCaps.Create(10, TimeSpan.FromHours(1), 1m, TimeSpan.FromDays(1), 10, 10).Value));

        Assert.Equal(CheckErrorCodes.PeopleOnly, Assert.Single(create.Errors).Code);
        Assert.Equal(CheckErrorCodes.PeopleOnly, Assert.Single(revise.Errors).Code);
        Assert.Equal(CheckErrorCodes.PeopleOnly, Assert.Single(activate.Errors).Code);
        Assert.Equal(AgentCredentialErrorCodes.PeopleOnly, Assert.Single(issue.Errors).Code);
        Assert.Equal(UserErrorCodes.PeopleOnly, Assert.Single(user.Errors).Code);
    }

    // An engine is refused the other moves only people make: grouping an operation into a session's set, disabling and enabling a
    // user, revoking a credential and deactivating a check. Each refusal comes before anything is read, so no item needs to exist.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_engine_cannot_group_a_session_manage_users_revoke_a_credential_or_deactivate_a_check(string provider)
    {
        await using var scenario = await CheckScenario.CreateAsync(provider);
        await scenario.CreateAsync("no-superlatives", CheckScenario.NoSuperlatives());
        scenario.Initiator = SystemInitiator.Checks;
        await using var scope = scenario.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var grouped = await services.GetRequiredService<IChangeSetDirectory>().AddToSessionAsync(
            "tidy-up",
            new NewChangeSet("Tidy up.", []),
            ChangeOperation.Create(OperationKind.Create, ItemKind.Entry, Guid.NewGuid(), null, 1).Value);
        var disabled = await services.GetRequiredService<IUserDirectory>().DisableAsync(Guid.NewGuid());
        var enabled = await services.GetRequiredService<IUserDirectory>().EnableAsync(Guid.NewGuid());
        var revoked = await services.GetRequiredService<IAgentCredentialDirectory>().RevokeAsync(Guid.NewGuid());
        var deactivated = await scenario.RunAsync(directory => directory.DeactivateAsync("no-superlatives"));

        Assert.Equal(ChangeSetErrorCodes.InitiatorNotAllowed, Assert.Single(grouped.Errors).Code);
        Assert.Equal(UserErrorCodes.PeopleOnly, Assert.Single(disabled.Errors).Code);
        Assert.Equal(UserErrorCodes.PeopleOnly, Assert.Single(enabled.Errors).Code);
        Assert.Equal(AgentCredentialErrorCodes.PeopleOnly, Assert.Single(revoked.Errors).Code);
        Assert.Equal(CheckErrorCodes.PeopleOnly, Assert.Single(deactivated.Errors).Code);
    }

    // AUD-04: the retention purge runs as the retention engine, and no other engine may purge.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task The_retention_engine_purges_stored_prompts_and_logs_each_as_itself(string provider)
    {
        await using var scenario = await ModelScenario.CreateAsync(provider, new() { ["Models:PromptRetention"] = "1.00:00:00" });
        var old = (await scenario.AskAsync(ModelScenario.Tone)).Value;
        scenario.Clock.Now = scenario.Clock.Now.AddDays(2);

        scenario.Initiator = SystemInitiator.Checks;
        var byChecks = await scenario.RunAsync(services => services.GetRequiredService<IModelCallLog>().PurgeAsync());
        scenario.Initiator = SystemInitiator.Retention;
        var byRetention = await scenario.RunAsync(services => services.GetRequiredService<IModelCallLog>().PurgeAsync());

        Assert.Equal(ModelErrorCodes.PeopleOnly, Assert.Single(byChecks.Errors).Code);
        Assert.Equal(1, byRetention.Value);
        var purge = Assert.Single(await scenario.PurgeEventsAsync());
        Assert.Equal((old.CallId, "system", "retention"), (purge.SubjectId!.Value, purge.InitiatorKind, purge.InitiatorName));
    }

    // A job an engine enqueues, such as the retention purge scheduling its next run, records that engine.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_job_an_engine_enqueues_is_stored_as_that_engine(string provider)
    {
        await using var scenario = await CheckScenario.CreateAsync(provider);
        scenario.Initiator = SystemInitiator.Retention;
        await using var scope = scenario.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        Job enqueued;
        await using (var work = await services.GetRequiredService<IUnitOfWork>().BeginAsync())
        {
            enqueued = await services.GetRequiredService<IJobQueue>().EnqueueAsync(new NewJob("retention.purge", JsonDocument.Parse("{}").RootElement.Clone()));
            await work.CommitAsync();
        }

        Assert.Equal(SystemInitiator.Retention, (await services.GetRequiredService<IJobQueue>().GetAsync(enqueued.Id)).Value.Initiator);
    }

    // EXT-03: a handler Core does not list cannot borrow an engine's permissions by naming it in RunAs; its job fails without running,
    // and says why. The check run, which Core lists, runs as the check engine (ChangeSetChecksTests).
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_handler_that_claims_an_engine_it_is_not_listed_for_fails_its_job_unrun(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(
            provider,
            settings: FastQueue,
            configure: builder => builder.Services.AddFluentCmsExtension<IJobHandler, EngineHandler>());

        var job = await agent.AsAgentAsync(services => EnqueueAsync(services, EngineHandler.Name));
        var ended = await EndedAsync(agent, job.Id);

        Assert.Equal(JobState.Failed, ended.State);
        Assert.StartsWith(JobErrorCodes.EngineNotAllowed, ended.LastError);
        Assert.False(EngineHandler.RanAs.ContainsKey(job.Id));
    }

    // ADR-0012: a reset link takes over the account it resets, so only a person issues one. Code running inside the check engine's
    // scope, as a check type does, is refused, and cannot pass the engine on to a job of a handler Core does not list for it either.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Code_running_as_an_engine_cannot_issue_a_reset_link_or_pass_the_engine_to_a_job(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(
            provider,
            settings: FastQueue,
            configure: builder =>
            {
                builder.Services.AddFluentCmsExtension<ICheckType, EngineProbe>();
                builder.Services.AddFluentCmsExtension<IJobHandler, Follower>();
            });
        await agent.CreateAsPersonAsync("probe");
        var definition = new CheckDefinition(
            EngineProbe.Name,
            "Probes what the check engine may do.",
            JsonSerializer.SerializeToElement(new { }),
            [
                new CheckExample("passes", CheckVerdict.Pass, JsonSerializer.SerializeToElement(new { title = "A kettle" }), contentType: "probe"),
                new CheckExample("fails", CheckVerdict.Fail, JsonSerializer.SerializeToElement(new { title = "fail" }), contentType: "probe"),
            ],
            scope: new CheckScope(["probe"]));
        await agent.AsPersonAsync<ICheckDirectory, Check>(directory => directory.CreateAsync(new NewCheck("engine-probe", definition)));
        await agent.AsPersonAsync<ICheckDirectory, Check>(directory => directory.ActivateAsync("engine-probe", 1));
        var byAgent = await agent.AsAgentAsync(services => services.GetRequiredService<IPasswordReset>().IssueAsync(Guid.NewGuid()));

        var created = await agent.CallAsync("create_entry", new()
        {
            ["content_type"] = "probe",
            ["fields"] = new JsonObject { ["title"] = "A kettle" },
            ["session_id"] = "probe",
            ["rationale"] = "Probe the engine.",
        });
        await agent.CallAsync("submit_change_set", new() { ["id"] = (string?)created["changeSet"]!["id"] });
        var followed = await EndedAsync(agent, Assert.Single(EngineProbe.Enqueued));

        Assert.Equal(UserErrorCodes.PeopleOnly, Assert.Single(byAgent.Errors).Code);
        Assert.Equal(UserErrorCodes.PeopleOnly, Assert.Single(EngineProbe.Refusals));
        Assert.Equal(JobState.Failed, followed.State);
        Assert.StartsWith(JobErrorCodes.EngineNotAllowed, followed.LastError);
        Assert.Empty(Follower.Ran);
        Assert.DoesNotContain(await agent.QueryAsync(context => context.AuditEvents.AsNoTracking().ToListAsync()), row => row.Operation == "user.password_reset_issued");
    }

    private static readonly Dictionary<string, string?> FastQueue = new() { ["JobQueue:PollingInterval"] = "00:00:00.050" };

    private static async Task<Job> EnqueueAsync(IServiceProvider services, string type)
    {
        await using var work = await services.GetRequiredService<IUnitOfWork>().BeginAsync();
        var enqueued = await services.GetRequiredService<IJobQueue>().EnqueueAsync(new NewJob(type, JsonDocument.Parse("{}").RootElement.Clone()));
        await work.CommitAsync();
        return enqueued;
    }

    private static async Task<Job> EndedAsync(ConnectedAgent agent, Guid jobId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        Job read;
        do
        {
            await Task.Delay(50);
            read = await agent.AsAgentAsync(async services => (await services.GetRequiredService<IJobQueue>().GetAsync(jobId)).Value);
        }
        while (read.State is JobState.Queued or JobState.Running && DateTime.UtcNow < deadline);

        return read;
    }

    // A component's name and id are stored together, and a row whose id is not its component's is refused as a fault.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_engine_is_stored_by_its_component_whatever_its_display_name(string provider)
    {
        await using var scenario = await AuditScenario.CreateAsync(provider);
        var renamed = SystemInitiator.Checks with { DisplayName = "The check engine" };

        await scenario.AppendAsync(DateTimeOffset.UnixEpoch.AddDays(1), new NewAuditEvent("checks.renamed") { SubjectType = Set.Type, SubjectId = Set.Id }, renamed);

        var read = Assert.Single(await scenario.ReadAsync(new AuditQuery { Subject = Set }));
        Assert.Equal("checks", Assert.IsType<SystemInitiator>(read.Initiator).Component);
        Assert.Throws<InvalidOperationException>(() => InitiatorKinds.RestoreSystem("checks", SystemInitiator.Retention.Id));
        Assert.Throws<InvalidOperationException>(() => InitiatorKinds.RestoreSystem("Not A Component", Guid.NewGuid()));

        // Read back through the log, a row whose id is not its component's is a fault, not an engine.
        await QueryAsync(scenario, context => context.AuditEvents
            .Where(row => row.Operation == "checks.renamed")
            .ExecuteUpdateAsync(row => row.SetProperty(stored => stored.InitiatorId, SystemInitiator.Retention.Id)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.ReadAsync(new AuditQuery { Subject = Set }));
    }

    // A job row whose engine id is not its component's is refused when it is read, as an audit row is.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_job_stored_with_another_engine_s_id_is_refused_when_it_is_read(string provider)
    {
        await using var scenario = await CheckScenario.CreateAsync(provider);
        scenario.Initiator = SystemInitiator.Retention;
        await using var scope = scenario.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        Job enqueued;
        await using (var work = await services.GetRequiredService<IUnitOfWork>().BeginAsync())
        {
            enqueued = await services.GetRequiredService<IJobQueue>().EnqueueAsync(new NewJob("retention.purge", JsonDocument.Parse("{}").RootElement.Clone()));
            await work.CommitAsync();
        }

        await scenario.QueryAsync(context => context.Jobs
            .Where(row => row.Id == enqueued.Id)
            .ExecuteUpdateAsync(row => row.SetProperty(stored => stored.InitiatorId, SystemInitiator.Checks.Id)));

        await using var reading = scenario.Services.CreateAsyncScope();
        await Assert.ThrowsAsync<InvalidOperationException>(() => reading.ServiceProvider.GetRequiredService<IJobQueue>().GetAsync(enqueued.Id));
    }

    private static async Task<T> QueryAsync<T>(AuditScenario scenario, Func<FluentCmsDbContext, Task<T>> query)
    {
        await using var scope = scenario.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>());
    }

    // A check type that, run as the check engine, tries what only a person does and to enqueue a job for a handler Core does not list.
    private sealed class EngineProbe : ICheckType
    {
        public const string Name = "sample.engine-probe";

        public static ConcurrentQueue<string> Refusals { get; } = new();

        public static ConcurrentQueue<Guid> Enqueued { get; } = new();

        string ICheckType.Name => Name;

        public CheckKind Kind => CheckKind.Deterministic;

        public IReadOnlyList<Error> ValidateSettings(JsonElement settings)
        {
            return new CheckSettingsReader(Name, settings).Errors();
        }

        public async Task<CheckOutcome> EvaluateAsync(CheckInput input, CancellationToken cancellationToken)
        {
            var services = input.Services;
            if (services.GetRequiredService<ICurrentInitiator>().Current is SystemInitiator)
            {
                var reset = await services.GetRequiredService<IPasswordReset>().IssueAsync(Guid.NewGuid(), cancellationToken);
                Refusals.Enqueue(reset.Errors.FirstOrDefault()?.Code ?? "issued");
                Enqueued.Enqueue((await EnqueueAsync(services, Follower.Name)).Id);
            }

            return input.Subject.Fields.GetRawText().Contains("fail", StringComparison.Ordinal) ? CheckOutcome.Fail("Remove 'fail'.") : CheckOutcome.Pass();
        }
    }

    private sealed class Follower : IJobHandler
    {
        public const string Name = "test.follower";

        public static ConcurrentQueue<Guid> Ran { get; } = new();

        public string Type => Name;

        public Task<Result> RunAsync(JobContext context, CancellationToken cancellationToken)
        {
            Ran.Enqueue(context.Job.Id);
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class EngineHandler : IJobHandler
    {
        public const string Name = "test.engine";

        public static ConcurrentDictionary<Guid, Initiator?> RanAs { get; } = new();

        public string Type => Name;

        public SystemInitiator RunAs => SystemInitiator.Checks;

        public Task<Result> RunAsync(JobContext context, CancellationToken cancellationToken)
        {
            RanAs[context.Job.Id] = context.Services.GetRequiredService<ICurrentInitiator>().Current;
            return Task.FromResult(Result.Success());
        }
    }
}
