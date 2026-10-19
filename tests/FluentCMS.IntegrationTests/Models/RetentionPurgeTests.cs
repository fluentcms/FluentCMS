using System.Text.Json;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Models;
using FluentCMS.Infrastructure.Jobs;
using FluentCMS.Infrastructure.Models;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Models;

// AUD-04: stored prompts past their retention are purged without anyone asking, by the retention engine, a batch at a time, and
// one run is always scheduled.
public sealed class RetentionPurgeTests
{
    private const string JobType = "retention.purge";

    // How many calls one purge clears, as the call log sets it.
    private const int Batch = 1000;

    // A started host's worker runs the purge as the retention engine, which clears what is past its retention and schedules the next
    // run; a second host that starts then finds that run queued and enqueues none.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_started_host_runs_the_purge_as_the_retention_engine_and_keeps_one_run_scheduled(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        var settings = new Dictionary<string, string?>(database.Configuration) { ["JobQueue:PollingInterval"] = "00:00:00.050" };
        var expired = Guid.CreateVersion7();
        await using (var first = await AdminHost.StartAsync(install, settings: settings))
        {
            // The first host only migrates the database. Its own run ends before the expired call goes in, so the second host's run
            // is the one that purges it.
            await UntilAsync(first, context => context.Jobs.AnyAsync(row => row.Type == JobType && row.State == JobState.Succeeded));
            await QueryAsync(first, context =>
            {
                context.ModelCalls.Add(ExpiredCall(expired));
                return context.SaveChangesAsync();
            });
            await QueryAsync(first, context => context.Jobs.Where(row => row.Type == JobType).ExecuteDeleteAsync());
        }

        await using var host = await AdminHost.StartAsync(install, settings: settings);
        await UntilAsync(host, context => context.Jobs.AnyAsync(row => row.Type == JobType && row.State == JobState.Succeeded));

        await using var second = await AdminHost.StartAsync(install, settings: new Dictionary<string, string?>(database.Configuration) { ["JobQueue:PollingInterval"] = "01:00:00" });

        var call = await QueryAsync(host, context => context.ModelCalls.AsNoTracking().SingleAsync(row => row.Id == expired));
        Assert.Equal(((string?)null, true), (call.Prompt, call.PurgedAt is not null));
        var purged = await QueryAsync(host, context => context.AuditEvents.AsNoTracking().SingleAsync(row => row.Operation == "model_call.purged"));
        Assert.Equal(("system", "retention"), (purged.InitiatorKind, purged.InitiatorName));
        var runs = await QueryAsync(host, context => context.Jobs.AsNoTracking().Where(row => row.Type == JobType).ToListAsync());
        Assert.Equal(JobState.Succeeded, Assert.Single(runs, row => row.State != JobState.Queued).State);
        var next = Assert.Single(runs, row => row.State == JobState.Queued);
        Assert.InRange(next.ScheduledAt, DateTimeOffset.UtcNow.AddHours(23), DateTimeOffset.UtcNow.AddHours(25));
        Assert.All(runs, row => Assert.Equal(("system", "retention"), (row.InitiatorKind, row.InitiatorName)));
    }

    // Two runs at once, as when two hosts start together, each see the other running, and each still leaves a run queued.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Two_runs_at_once_never_leave_the_schedule_empty(string provider)
    {
        await using var scenario = await ModelScenario.CreateAsync(provider);
        scenario.Initiator = SystemInitiator.Retention;
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        await scenario.QueryAsync(context =>
        {
            context.Jobs.AddRange(RunningRun(first, scenario.Clock.Now), RunningRun(second, scenario.Clock.Now));
            return context.SaveChangesAsync();
        });

        await Task.WhenAll(RunAsync(scenario, first, []), RunAsync(scenario, second, []));

        Assert.Contains(await RunsAsync(scenario), row => row.State == JobState.Queued);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_run_purges_every_prompt_past_the_retention_in_batches_and_schedules_the_next(string provider)
    {
        await using var scenario = await ModelScenario.CreateAsync(provider, new() { ["Models:PromptRetention"] = "30.00:00:00" });
        Assert.True((await scenario.AskAsync(ModelScenario.Tone)).IsSuccess);
        await CopyCallAsync(scenario, Batch + 100);
        scenario.Clock.Now = scenario.Clock.Now.AddDays(31);
        scenario.Initiator = SystemInitiator.Retention;
        var reported = new List<int>();

        var result = await RunAsync(scenario, Guid.CreateVersion7(), reported);

        Assert.True(result.IsSuccess, string.Join(" ", result.Errors.Select(error => error.Message)));
        Assert.Equal([Batch, Batch + 101, Batch + 101], reported);
        Assert.All(await scenario.CallRowsAsync(), row => Assert.Equal(((string?)null, true), (row.Prompt, row.PurgedAt is not null)));
        var events = await scenario.PurgeEventsAsync();
        Assert.Equal(Batch + 101, events.Count);
        Assert.All(events, row => Assert.Equal(("system", "retention"), (row.InitiatorKind, row.InitiatorName)));

        var next = Assert.Single(await RunsAsync(scenario));
        Assert.Equal((JobState.Queued, scenario.Clock.Now.AddDays(1)), (next.State, next.ScheduledAt));
        Assert.Equal(("system", "retention"), (next.InitiatorKind, next.InitiatorName));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_run_that_finds_a_run_queued_schedules_none(string provider)
    {
        await using var scenario = await ModelScenario.CreateAsync(provider);
        await scenario.RunAsync(async services =>
        {
            await services.GetRequiredService<IRetentionSchedule>().EnsureScheduledAsync();
            return true;
        });
        var queued = Assert.Single(await RunsAsync(scenario)).Id;
        scenario.Initiator = SystemInitiator.Retention;

        var ran = await RunAsync(scenario, Guid.CreateVersion7(), []);

        Assert.True(ran.IsSuccess, string.Join(" ", ran.Errors.Select(error => error.Message)));
        var still = Assert.Single(await RunsAsync(scenario));
        Assert.Equal((queued, JobState.Queued), (still.Id, still.State));
    }

    // The next run is scheduled before the purge, so a run whose purge fails still leaves one coming.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_failed_purge_fails_the_run_with_its_reason(string provider)
    {
        await using var scenario = await ModelScenario.CreateAsync(provider, replace: services => services.AddScoped<IModelCallLog, FailingPurge>());
        scenario.Initiator = SystemInitiator.Retention;

        var failed = await RunAsync(scenario, Guid.CreateVersion7(), []);

        var error = Assert.Single(failed.Errors);
        Assert.Equal((FailingPurge.Code, FailingPurge.Message), (error.Code, error.Message));
        Assert.Equal(JobState.Queued, Assert.Single(await RunsAsync(scenario)).State);
    }

    // The worker runs the purge only as the retention engine; run as any other, it purges and schedules nothing.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_run_as_another_engine_purges_and_schedules_nothing(string provider)
    {
        await using var scenario = await ModelScenario.CreateAsync(provider);
        scenario.Initiator = SystemInitiator.Checks;

        var refused = await RunAsync(scenario, Guid.CreateVersion7(), []);

        Assert.Equal(ModelErrorCodes.PeopleOnly, Assert.Single(refused.Errors).Code);
        Assert.Empty(await RunsAsync(scenario));
    }

    private static async Task<FluentCMS.Core.Results.Result> RunAsync(ModelScenario scenario, Guid jobId, List<int> reported)
    {
        return await scenario.RunAsync(async services =>
        {
            var handler = services.GetServices<IJobHandler>().Single(candidate => candidate.Type == JobType);
            var job = new Job
            {
                Id = jobId,
                Type = JobType,
                Payload = JsonSerializer.SerializeToElement(new { }),
                State = JobState.Running,
                Attempts = 1,
                MaxAttempts = 3,
                CreatedAt = scenario.Clock.Now,
                ScheduledAt = scenario.Clock.Now,
            };

            return await handler.RunAsync(
                new JobContext(job, services, (progress, _) =>
                {
                    reported.Add(progress.Done);
                    return Task.FromResult(true);
                }),
                CancellationToken.None);
        });
    }

    private static Task<List<JobRow>> RunsAsync(ModelScenario scenario)
    {
        return scenario.QueryAsync(context => context.Jobs.AsNoTracking().Where(row => row.Type == JobType).ToListAsync());
    }

    private static async Task UntilAsync(AdminHost host, Func<FluentCmsDbContext, Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!await QueryAsync(host, condition))
        {
            Assert.True(DateTime.UtcNow < deadline, "The host's retention run did not finish within 30 seconds.");
            await Task.Delay(100);
        }
    }

    private static async Task<T> QueryAsync<T>(AdminHost host, Func<FluentCmsDbContext, Task<T>> query)
    {
        await using var scope = host.App.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>());
    }

    private static ModelCallRow ExpiredCall(Guid id)
    {
        return new ModelCallRow
        {
            Id = id,
            Timestamp = DateTimeOffset.UtcNow.AddDays(-100),
            Profile = "checks",
            Provider = "openai",
            Model = "gpt-fake",
            Purpose = "check:tone",
            InitiatorKind = "user",
            InitiatorId = Guid.NewGuid(),
            PromptHash = new string('a', 64),
            Prompt = "Decide whether the content is calm.",
            Response = """{"verdict": "pass"}""",
            Outcome = ModelCallOutcome.Succeeded,
        };
    }

    private static JobRow RunningRun(Guid id, DateTimeOffset at)
    {
        return new JobRow
        {
            Id = id,
            Type = JobType,
            Payload = "{}",
            SubjectType = "retention",
            SubjectId = SystemInitiator.Retention.Id,
            InitiatorKind = "system",
            InitiatorId = SystemInitiator.Retention.Id,
            InitiatorName = "retention",
            State = JobState.Running,
            Attempts = 1,
            MaxAttempts = 3,
            CreatedAt = at,
            ScheduledAt = at,
        };
    }

    // A call log whose purge fails, as when the database refuses it; a run uses nothing else of it.
    private sealed class FailingPurge : IModelCallLog
    {
        public const string Code = "model_purge_failed";
        public const string Message = "The stored prompts could not be cleared.";

        public Task AppendAsync(ModelCall call, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task CompleteAsync(ModelCall call, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<decimal> SpendAsync(Guid agentCredentialId, DateTimeOffset since, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<ModelCall>> FindByPromptHashAsync(string promptHash, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<FluentCMS.Core.Results.Result<int>> PurgeAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(FluentCMS.Core.Results.Result<int>.Failure(Code, Message));
        }
    }

    // Copies of the one recorded call, so a purge has more than a batch to clear.
    private static Task<int> CopyCallAsync(ModelScenario scenario, int copies)
    {
        return scenario.QueryAsync(async context =>
        {
            var template = await context.ModelCalls.AsNoTracking().SingleAsync();
            for (var copy = 0; copy < copies; copy++)
            {
                var row = (ModelCallRow)context.Entry(template).CurrentValues.ToObject();
                row.Id = Guid.CreateVersion7();
                context.ModelCalls.Add(row);
            }

            return await context.SaveChangesAsync();
        });
    }
}
