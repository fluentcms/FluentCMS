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

    [Fact]
    public async Task A_started_host_keeps_one_retention_run_scheduled_as_the_retention_engine()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install, settings: new Dictionary<string, string?> { ["JobQueue:PollingInterval"] = "01:00:00" });
        var administrator = await host.CreateFirstAdministratorAsync();

        await host.AsUserAsync<IRetentionSchedule, bool>(administrator, async schedule =>
        {
            await schedule.EnsureScheduledAsync();
            return true;
        });

        var runs = await host.AsUserAsync<FluentCmsDbContext, List<JobRow>>(
            administrator,
            context => context.Jobs.AsNoTracking().Where(row => row.Type == JobType).ToListAsync());
        Assert.Single(runs, row => row.State == JobState.Queued);
        Assert.All(runs, row => Assert.Equal(("system", "retention"), (row.InitiatorKind, row.InitiatorName)));
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
    public async Task A_run_that_finds_a_run_queued_schedules_none_and_a_failed_purge_fails_it(string provider)
    {
        await using var scenario = await ModelScenario.CreateAsync(provider);
        await scenario.RunAsync(async services =>
        {
            await services.GetRequiredService<IRetentionSchedule>().EnsureScheduledAsync();
            await services.GetRequiredService<IRetentionSchedule>().EnsureScheduledAsync();
            return true;
        });

        // Another engine may not purge, so the run fails with the reason, and the run already queued stays the only one.
        scenario.Initiator = SystemInitiator.Checks;
        var failed = await RunAsync(scenario, Guid.CreateVersion7(), []);

        Assert.Equal(ModelErrorCodes.PeopleOnly, Assert.Single(failed.Errors).Code);
        Assert.Single(await RunsAsync(scenario));
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
