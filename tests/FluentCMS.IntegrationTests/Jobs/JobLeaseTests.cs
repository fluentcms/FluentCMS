using System.Text.Json;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Persistence;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Jobs;

public sealed class JobLeaseTests
{
    private static readonly UserInitiator Amir = new(Guid.NewGuid(), "Amir");
    private static readonly DateTimeOffset Start = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(1);

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_job_commits_with_the_unit_of_work_that_enqueued_it_and_not_without_it(string provider)
    {
        await using var scenario = await Scenario.CreateAsync(provider);
        var subject = new JobSubject("change_set", Guid.NewGuid());

        var committed = await scenario.EnqueueAsync(new NewJob("example", Json("""{"items": 3}""")) { Subject = subject });
        var rolledBack = await scenario.EnqueueAsync(new NewJob("example", Json("{}")), commit: false);
        var outside = await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.RunAsync(queue => queue.EnqueueAsync(new NewJob("example", Json("{}")))));

        var stored = (await scenario.RunAsync(queue => queue.GetAsync(committed.Id))).Value;
        Assert.Equal(JobState.Queued, stored.State);
        Assert.Equal(Amir, stored.Initiator);
        Assert.Equal(subject, stored.Subject);
        Assert.Equal(3, stored.Payload.GetProperty("items").GetInt32());
        Assert.Equal((0, NewJob.DefaultMaxAttempts), (stored.Attempts, stored.MaxAttempts));
        Assert.Equal(Start, stored.ScheduledAt);
        Assert.Equal(JobErrorCodes.JobNotFound, Assert.Single((await scenario.RunAsync(queue => queue.GetAsync(rolledBack.Id))).Errors).Code);
        Assert.Contains("unit of work", outside.Message);
        Assert.Equal([committed.Id], (await scenario.RunAsync(queue => queue.ListForSubjectAsync(subject))).Select(job => job.Id));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_due_job_is_claimed_once_with_an_attempt_counted_and_a_later_one_waits_for_its_time(string provider)
    {
        await using var scenario = await Scenario.CreateAsync(provider);
        var later = await scenario.EnqueueAsync(new NewJob("example", Json("{}")) { RunAt = Start.AddMinutes(10) });
        var now = await scenario.EnqueueAsync(new NewJob("example", Json("{}")));

        var first = await scenario.ClaimAsync("worker-a");
        var none = await scenario.ClaimAsync("worker-b");
        Assert.True(await scenario.LeasesAsync(leases => leases.SucceedAsync(first!.Id, "worker-a")));
        scenario.Clock.Now = Start.AddMinutes(10);
        var second = await scenario.ClaimAsync("worker-b");

        Assert.Equal(now.Id, first!.Id);
        Assert.Equal((JobState.Running, 1, Start), (first.State, first.Attempts, first.StartedAt!.Value));
        Assert.Null(none);
        Assert.Equal(later.Id, second!.Id);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Of_workers_claiming_at_the_same_time_each_job_goes_to_one_of_them(string provider)
    {
        await using var scenario = await Scenario.CreateAsync(provider);
        var jobs = new List<Guid>();
        for (var index = 0; index < 6; index++)
        {
            jobs.Add((await scenario.EnqueueAsync(new NewJob("example", Json("{}")))).Id);
        }

        var claims = await Task.WhenAll(Enumerable.Range(0, 4).Select(worker => Task.Run(async () =>
        {
            var claimed = new List<Guid>();
            while (await scenario.ClaimAsync($"worker-{worker}") is { } job)
            {
                claimed.Add(job.Id);
            }

            return claimed;
        })));

        var claimedIds = claims.SelectMany(claimed => claimed).ToList();
        Assert.Equal(jobs.Order(), claimedIds.Order());
        foreach (var id in jobs)
        {
            Assert.Equal(1, (await scenario.RunAsync(queue => queue.GetAsync(id))).Value.Attempts);
        }
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_job_whose_lease_ran_out_goes_to_another_worker_and_the_first_one_changes_nothing(string provider)
    {
        await using var scenario = await Scenario.CreateAsync(provider);
        var job = await scenario.EnqueueAsync(new NewJob("example", Json("{}")));
        await scenario.ClaimAsync("worker-a");

        scenario.Clock.Now = Start.AddSeconds(30);
        Assert.True(await scenario.LeasesAsync(leases => leases.ReportProgressAsync(job.Id, "worker-a", new JobProgress(1, 4, "One of four."), Lease)));
        scenario.Clock.Now = Start.AddSeconds(80);
        Assert.Null(await scenario.ClaimAsync("worker-b"));

        scenario.Clock.Now = Start.AddMinutes(2);
        var reclaimed = await scenario.ClaimAsync("worker-b");
        var lateProgress = await scenario.LeasesAsync(leases => leases.ReportProgressAsync(job.Id, "worker-a", new JobProgress(4, 4, null), Lease));
        var lateSuccess = await scenario.LeasesAsync(leases => leases.SucceedAsync(job.Id, "worker-a"));
        var success = await scenario.LeasesAsync(leases => leases.SucceedAsync(job.Id, "worker-b"));

        Assert.Equal((job.Id, 2), (reclaimed!.Id, reclaimed.Attempts));
        Assert.False(lateProgress);
        Assert.False(lateSuccess);
        Assert.True(success);
        var stored = (await scenario.RunAsync(queue => queue.GetAsync(job.Id))).Value;
        Assert.Equal(JobState.Succeeded, stored.State);
        Assert.Equal(new JobProgress(1, 4, "One of four."), stored.Progress);
        Assert.Equal(Start.AddMinutes(2), stored.FinishedAt);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_retried_job_waits_for_its_delay_and_after_its_last_attempt_is_dead(string provider)
    {
        await using var scenario = await Scenario.CreateAsync(provider);
        var job = await scenario.EnqueueAsync(new NewJob("example", Json("{}")) { MaxAttempts = 2 });

        await scenario.ClaimAsync("worker-a");
        Assert.True(await scenario.LeasesAsync(leases => leases.RetryAsync(job.Id, "worker-a", "The catalog did not answer.", TimeSpan.FromSeconds(30))));
        var waiting = (await scenario.RunAsync(queue => queue.GetAsync(job.Id))).Value;
        var early = await scenario.ClaimAsync("worker-a");

        scenario.Clock.Now = Start.AddSeconds(30);
        Assert.Equal(2, (await scenario.ClaimAsync("worker-a"))!.Attempts);
        Assert.True(await scenario.LeasesAsync(leases => leases.RetryAsync(job.Id, "worker-a", "The catalog did not answer again.", TimeSpan.FromSeconds(60))));

        Assert.Equal((JobState.Queued, Start.AddSeconds(30), "The catalog did not answer."), (waiting.State, waiting.ScheduledAt, waiting.LastError));
        Assert.Null(early);
        var dead = (await scenario.RunAsync(queue => queue.GetAsync(job.Id))).Value;
        Assert.Equal((JobState.Dead, "The catalog did not answer again."), (dead.State, dead.LastError));
        Assert.Null(await scenario.ClaimAsync("worker-a"));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_failed_job_is_final_and_one_whose_worker_stopped_on_its_last_attempt_is_dead(string provider)
    {
        await using var scenario = await Scenario.CreateAsync(provider);
        var failing = await scenario.EnqueueAsync(new NewJob("example", Json("{}")));
        var stopping = await scenario.EnqueueAsync(new NewJob("example", Json("{}")) { MaxAttempts = 1 });

        await scenario.ClaimAsync("worker-a");
        Assert.True(await scenario.LeasesAsync(leases => leases.FailAsync(failing.Id, "worker-a", "item_not_found: There is no entity 42.")));
        await scenario.ClaimAsync("worker-a");
        scenario.Clock.Now = Start.AddMinutes(5);
        Assert.Null(await scenario.ClaimAsync("worker-b"));

        var failed = (await scenario.RunAsync(queue => queue.GetAsync(failing.Id))).Value;
        var dead = (await scenario.RunAsync(queue => queue.GetAsync(stopping.Id))).Value;
        Assert.Equal((JobState.Failed, 1), (failed.State, failed.Attempts));
        Assert.Equal(JobState.Dead, dead.State);
        Assert.Contains("lease ran out on attempt 1 of 1", dead.LastError);
    }

    private static JsonElement Json(string json)
    {
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private sealed class Scenario(TemporaryDatabase database, ServiceProvider services, ManualClock clock) : IAsyncDisposable
    {
        public ManualClock Clock { get; } = clock;

        public static async Task<Scenario> CreateAsync(string provider)
        {
            var database = await TemporaryDatabase.CreateAsync(provider);
            var clock = new ManualClock(Start);
            var services = TestServices.Build(
                database.Configuration,
                collection => collection.AddSingleton<ICurrentInitiator>(new TestInitiator(Amir)).AddSingleton<TimeProvider>(clock));
            await TestServices.MigrateAsync(services);
            return new Scenario(database, services, clock);
        }

        public async Task<Job> EnqueueAsync(NewJob job, bool commit = true)
        {
            await using var scope = services.CreateAsyncScope();
            await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync();
            var enqueued = await scope.ServiceProvider.GetRequiredService<IJobQueue>().EnqueueAsync(job);
            if (commit)
            {
                await work.CommitAsync();
            }

            return enqueued;
        }

        public Task<Job?> ClaimAsync(string owner)
        {
            return LeasesAsync(leases => leases.ClaimAsync(owner, Lease));
        }

        public async Task<T> RunAsync<T>(Func<IJobQueue, Task<T>> action)
        {
            await using var scope = services.CreateAsyncScope();
            return await action(scope.ServiceProvider.GetRequiredService<IJobQueue>());
        }

        public async Task<T> LeasesAsync<T>(Func<IJobLeases, Task<T>> action)
        {
            await using var scope = services.CreateAsyncScope();
            return await action(scope.ServiceProvider.GetRequiredService<IJobLeases>());
        }

        public async ValueTask DisposeAsync()
        {
            await services.DisposeAsync();
            await database.DisposeAsync();
        }
    }
}
