using System.Collections.Concurrent;
using System.Text.Json;
using FluentCMS.Core.Extensions;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Host.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Jobs;

public sealed class JobQueueWorkerTests
{
    // Short enough that a test waits a moment, not the shipped five seconds and thirty seconds.
    private static readonly Dictionary<string, string?> FastQueue = new()
    {
        ["JobQueue:PollingInterval"] = "00:00:00.050",
        ["JobQueue:RetryDelay"] = "00:00:00.050",
    };

    [Fact]
    public async Task The_worker_runs_each_job_as_its_initiator_and_records_how_it_ended()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install, settings: FastQueue, configure: builder =>
        {
            builder.Services.AddFluentCmsExtension<IJobHandler, CountingHandler>();
            builder.Services.AddFluentCmsExtension<IJobHandler, RefusingHandler>();
            builder.Services.AddFluentCmsExtension<IJobHandler, ThrowingHandler>();
        });
        var administrator = await host.CreateFirstAdministratorAsync();

        var counting = await EnqueueAsync(host, administrator, new NewJob(CountingHandler.Name, JsonDocument.Parse("""{"items": 3}""").RootElement.Clone()));
        var refusing = await EnqueueAsync(host, administrator, new NewJob(RefusingHandler.Name, default));
        var throwing = await EnqueueAsync(host, administrator, new NewJob(ThrowingHandler.Name, default) { MaxAttempts = 2 });
        var unknown = await EnqueueAsync(host, administrator, new NewJob("no-such-type", default));

        var succeeded = await FinishedAsync(host, administrator, counting.Id);
        var failed = await FinishedAsync(host, administrator, refusing.Id);
        var dead = await FinishedAsync(host, administrator, throwing.Id);
        var orphan = await FinishedAsync(host, administrator, unknown.Id);

        Assert.Equal((JobState.Succeeded, 1), (succeeded.State, succeeded.Attempts));
        Assert.Equal(new JobProgress(3, 3, "Counted 3 of 3."), succeeded.Progress);
        Assert.Equal("Test administrator", CountingHandler.RanAs[counting.Id]);
        Assert.Equal((JobState.Failed, 1), (failed.State, failed.Attempts));
        Assert.Equal("item_not_found: There is no such item.", failed.LastError);
        Assert.Equal((JobState.Dead, 2, "The catalog did not answer."), (dead.State, dead.Attempts, dead.LastError));
        Assert.Equal(JobState.Dead, orphan.State);
        Assert.Contains("No handler runs jobs of the type 'no-such-type'", orphan.LastError);
    }

    [Fact]
    public async Task A_handler_that_works_past_its_lease_without_progress_keeps_the_job_and_runs_once()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(
            install,
            settings: new Dictionary<string, string?>(FastQueue) { ["JobQueue:LeaseDuration"] = "00:00:01.500", ["JobQueue:WorkerCount"] = "3" },
            configure: builder => builder.Services.AddFluentCmsExtension<IJobHandler, SlowHandler>());
        var administrator = await host.CreateFirstAdministratorAsync();

        var job = await EnqueueAsync(host, administrator, new NewJob(SlowHandler.Name, default));
        var finished = await FinishedAsync(host, administrator, job.Id);

        Assert.Equal((JobState.Succeeded, 1), (finished.State, finished.Attempts));
        Assert.Equal(1, SlowHandler.Runs[job.Id]);
    }

    [Fact]
    public void The_retry_delay_doubles_with_each_attempt_up_to_its_ceiling()
    {
        var settings = new JobQueueOptions { RetryDelay = TimeSpan.FromSeconds(30), MaxRetryDelay = TimeSpan.FromMinutes(3) };

        Assert.Equal(
            [TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120), TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3)],
            Enumerable.Range(1, 5).Select(attempt => JobQueueWorker.RetryDelay(settings, attempt)));
    }

    private static Task<Job> EnqueueAsync(AdminHost host, Guid administrator, NewJob job)
    {
        return host.AsUserAsync<IServiceProvider, Job>(administrator, async services =>
        {
            await using var work = await services.GetRequiredService<IUnitOfWork>().BeginAsync();
            var enqueued = await services.GetRequiredService<IJobQueue>().EnqueueAsync(job);
            await work.CommitAsync();
            return enqueued;
        });
    }

    private static async Task<Job> FinishedAsync(AdminHost host, Guid administrator, Guid id)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var job = (await host.AsUserAsync<IJobQueue, Result<Job>>(administrator, queue => queue.GetAsync(id))).Value;
            if (job.State is JobState.Succeeded or JobState.Failed or JobState.Dead || DateTime.UtcNow > deadline)
            {
                return job;
            }

            await Task.Delay(50);
        }
    }

    private sealed class CountingHandler : IJobHandler
    {
        public const string Name = "test.counting";

        public static ConcurrentDictionary<Guid, string> RanAs { get; } = new();

        public string Type => Name;

        public async Task<Result> RunAsync(JobContext context, CancellationToken cancellationToken)
        {
            RanAs[context.Job.Id] = context.Services.GetRequiredService<ICurrentInitiator>().Current?.DisplayName ?? "nobody";

            var items = context.Job.Payload.GetProperty("items").GetInt32();
            for (var done = 1; done <= items; done++)
            {
                await context.ReportProgressAsync(done, items, $"Counted {done} of {items}.", cancellationToken);
            }

            return Result.Success();
        }
    }

    private sealed class RefusingHandler : IJobHandler
    {
        public const string Name = "test.refusing";

        public string Type => Name;

        public Task<Result> RunAsync(JobContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult(Result.Failure("item_not_found", "There is no such item."));
        }
    }

    private sealed class SlowHandler : IJobHandler
    {
        public const string Name = "test.slow";

        public static ConcurrentDictionary<Guid, int> Runs { get; } = new();

        public string Type => Name;

        public async Task<Result> RunAsync(JobContext context, CancellationToken cancellationToken)
        {
            Runs.AddOrUpdate(context.Job.Id, 1, (_, runs) => runs + 1);
            await Task.Delay(TimeSpan.FromSeconds(3.5), cancellationToken);
            return Result.Success();
        }
    }

    private sealed class ThrowingHandler : IJobHandler
    {
        public const string Name = "test.throwing";

        public string Type => Name;

        public Task<Result> RunAsync(JobContext context, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("The catalog did not answer.");
        }
    }
}
