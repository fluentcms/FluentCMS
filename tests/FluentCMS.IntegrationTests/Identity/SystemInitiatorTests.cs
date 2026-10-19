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

    // A submission enqueues the check run in the agent's call; the job type, not whoever enqueued it, decides that it runs as the
    // check engine, and the job still records who enqueued it.
    [Fact]
    public async Task The_worker_runs_a_job_as_the_engine_its_handler_names()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(
            install,
            settings: new Dictionary<string, string?> { ["JobQueue:PollingInterval"] = "00:00:00.050" },
            configure: builder => builder.Services.AddFluentCmsExtension<IJobHandler, EngineHandler>());
        var administrator = await host.CreateFirstAdministratorAsync();

        var job = await host.AsUserAsync<IServiceProvider, Job>(administrator, async services =>
        {
            await using var work = await services.GetRequiredService<IUnitOfWork>().BeginAsync();
            var enqueued = await services.GetRequiredService<IJobQueue>().EnqueueAsync(new NewJob(EngineHandler.Name, JsonDocument.Parse("{}").RootElement.Clone()));
            await work.CommitAsync();
            return enqueued;
        });

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!EngineHandler.RanAs.ContainsKey(job.Id) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.Equal(SystemInitiator.Checks, EngineHandler.RanAs[job.Id]);
        Assert.IsType<UserInitiator>(job.Initiator);
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
