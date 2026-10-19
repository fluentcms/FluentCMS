using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using FluentCMS.Core.Extensions;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Host.Jobs;
using FluentCMS.Infrastructure.Jobs;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluentCMS.IntegrationTests.Jobs;

public sealed class JobQueueWorkerTests
{
    // Short enough that a test waits a moment, not the shipped five seconds and thirty seconds.
    private static readonly Dictionary<string, string?> FastQueue = new()
    {
        ["JobQueue:PollingInterval"] = "00:00:00.050",
        ["JobQueue:RetryDelay"] = "00:00:00.050",
    };

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

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

    // The test moves the clock past the lease twice over, each step only once the heartbeat has renewed the lease at the clock's time,
    // so the job stays this worker's whatever the machine's load.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_handler_that_works_past_its_lease_without_progress_keeps_the_job_and_runs_once(string provider)
    {
        // On a whole second, so the lease's end reads back exactly from PostgreSQL, which keeps microseconds.
        var clock = new ManualClock(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var host = await AdminHost.StartAsync(
            install,
            settings: Settings(database, lease: "00:00:03", workers: 3),
            configure: builder => builder.Services.AddSingleton<TimeProvider>(clock).AddFluentCmsExtension<IJobHandler, WaitingHandler>());
        var administrator = await host.CreateFirstAdministratorAsync();
        var job = await EnqueueAsync(host, administrator, new NewJob(WaitingHandler.Name, default));
        var run = WaitingHandler.For(job.Id);
        await run.Started.Task.WaitAsync(Timeout);

        // Steps under half the lease, so a renewal that read the clock just before a step still holds past the next one.
        foreach (var _ in Enumerable.Range(0, 8))
        {
            clock.Now += TimeSpan.FromSeconds(1);
            await UntilAsync(async () => (await JobRowAsync(host, job.Id)).LeaseUntil >= clock.Now + TimeSpan.FromSeconds(3));
        }

        run.Finish.SetResult();
        var finished = await FinishedAsync(host, administrator, job.Id);

        Assert.Equal((JobState.Succeeded, 1), (finished.State, finished.Attempts));
        Assert.Equal(1, run.Runs);
    }

    // A trigger makes the renewals fail while the clock stands still, so the lease holds; once they work again, the clock moves past
    // the lease twice over and the job is still this worker's.
    [Fact]
    public async Task A_running_worker_keeps_the_job_through_renewals_that_fail_and_runs_it_once()
    {
        var clock = new ManualClock(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(
            install,
            settings: new Dictionary<string, string?>(FastQueue) { ["JobQueue:LeaseDuration"] = "00:00:00.600", ["JobQueue:WorkerCount"] = "2" },
            configure: builder => builder.Services.AddSingleton<TimeProvider>(clock).AddFluentCmsExtension<IJobHandler, WaitingHandler>());
        var administrator = await host.CreateFirstAdministratorAsync();
        await QueryAsync(host, context => context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE "FailRenewals" ("Id" INTEGER PRIMARY KEY);
            INSERT INTO "FailRenewals" ("Id") VALUES (1);
            CREATE TRIGGER "FailRenewal" BEFORE UPDATE OF "LeaseUntil" ON "Jobs"
            WHEN OLD."State" = 'Running' AND NEW."State" = 'Running' AND EXISTS (SELECT 1 FROM "FailRenewals")
            BEGIN SELECT RAISE(ABORT, 'The database is unreachable.'); END;
            """));

        var job = await EnqueueAsync(host, administrator, new NewJob(WaitingHandler.Name, default));
        var run = WaitingHandler.For(job.Id);
        await run.Started.Task.WaitAsync(Timeout);
        await Task.Delay(TimeSpan.FromSeconds(1));
        await QueryAsync(host, context => context.Database.ExecuteSqlRawAsync("""DELETE FROM "FailRenewals";"""));

        foreach (var _ in Enumerable.Range(0, 6))
        {
            clock.Now += TimeSpan.FromMilliseconds(250);
            await UntilAsync(async () => (await JobRowAsync(host, job.Id)).LeaseUntil >= clock.Now + TimeSpan.FromMilliseconds(600));
        }

        run.Finish.SetResult();
        var finished = await FinishedAsync(host, administrator, job.Id);

        Assert.Equal((JobState.Succeeded, 1), (finished.State, finished.Attempts));
        Assert.Equal(1, run.Runs);
        Assert.False(run.Cancelled.Task.IsCompleted);
    }

    // The clock stands still, so only the stolen lease can stop the handler; the next job shows the worker has moved on.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_handler_whose_lease_another_worker_took_is_stopped_and_its_worker_records_nothing(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var host = await AdminHost.StartAsync(
            install,
            settings: Settings(database),
            configure: builder => builder.Services
                .AddSingleton<TimeProvider>(new ManualClock(DateTimeOffset.UtcNow))
                .AddFluentCmsExtension<IJobHandler, WaitingHandler>()
                .AddFluentCmsExtension<IJobHandler, CountingHandler>());
        var administrator = await host.CreateFirstAdministratorAsync();
        var job = await EnqueueAsync(host, administrator, new NewJob(WaitingHandler.Name, default));
        var run = WaitingHandler.For(job.Id);
        await run.Started.Task.WaitAsync(Timeout);

        await QueryAsync(host, context => context.Jobs
            .Where(row => row.Id == job.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.LeaseOwner, "another-worker")));
        await run.Cancelled.Task.WaitAsync(Timeout);
        var next = await EnqueueAsync(host, administrator, new NewJob(CountingHandler.Name, JsonDocument.Parse("""{"items": 1}""").RootElement.Clone()));
        Assert.Equal(JobState.Succeeded, (await FinishedAsync(host, administrator, next.Id)).State);

        var stolen = await JobRowAsync(host, job.Id);
        Assert.Equal((JobState.Running, "another-worker", 1), (stolen.State, stolen.LeaseOwner, stolen.Attempts));
        Assert.Null(stolen.LastError);
        Assert.Null(stolen.FinishedAt);
        Assert.Equal(1, run.Runs);
    }

    // A job allowed one attempt shows the attempt was given back: without that, the restart would find it dead.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_job_running_when_the_host_stops_is_queued_again_with_its_attempt_and_runs_after_a_restart(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        Guid administrator;
        Job job;
        WaitingHandler.Run run;
        await using (var host = await AdminHost.StartAsync(install, settings: Settings(database), configure: AddWaitingHandler))
        {
            administrator = await host.CreateFirstAdministratorAsync();
            job = await EnqueueAsync(host, administrator, new NewJob(WaitingHandler.Name, default) { MaxAttempts = 1 });
            run = WaitingHandler.For(job.Id);
            await run.Started.Task.WaitAsync(Timeout);

            await host.App.StopAsync();

            await run.Cancelled.Task.WaitAsync(Timeout);
            var released = await JobRowAsync(host, job.Id);
            Assert.Equal((JobState.Queued, 0, (string?)null, (DateTimeOffset?)null), (released.State, released.Attempts, released.LeaseOwner, released.LeaseUntil));
            Assert.True(released.ScheduledAt <= DateTimeOffset.UtcNow, "The released job is not due at once.");
        }

        run.Finish.SetResult();
        await using var restarted = await AdminHost.StartAsync(install, settings: Settings(database), configure: AddWaitingHandler);
        var finished = await FinishedAsync(restarted, administrator, job.Id);

        Assert.Equal((JobState.Succeeded, 1), (finished.State, finished.Attempts));
        Assert.Equal(2, run.Runs);
    }

    [Fact]
    public async Task A_renewal_that_throws_is_tried_again_at_the_next_beat()
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var lease = TimeSpan.FromMilliseconds(300);
        var calls = 0;
        var third = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var done = new CancellationTokenSource();

        var keeping = JobQueueWorker.KeepLeaseAsync(
            _ =>
            {
                var call = Interlocked.Increment(ref calls);
                if (call == 3)
                {
                    third.TrySetResult();
                }

                return call == 1 ? Task.FromException<bool>(new TimeoutException("The database did not answer.")) : Task.FromResult(true);
            },
            clock.Now + lease,
            lease,
            clock,
            NullLogger.Instance,
            done.Token);
        await third.Task.WaitAsync(Timeout);
        await done.CancelAsync();

        Assert.True(await keeping.WaitAsync(Timeout));
    }

    // The clock stands still while renewals fail, so the lease holds however many beats pass. Once it is past the deadline, the
    // lease is given up without trying another renewal.
    [Fact]
    public async Task Renewals_that_keep_failing_give_the_lease_up_at_its_deadline_and_not_before()
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var lease = TimeSpan.FromMilliseconds(300);
        var failures = 0;
        var third = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var keeping = JobQueueWorker.KeepLeaseAsync(
            _ =>
            {
                if (Interlocked.Increment(ref failures) == 3)
                {
                    third.TrySetResult();
                }

                throw new TimeoutException("The database did not answer.");
            },
            clock.Now + lease,
            lease,
            clock,
            NullLogger.Instance,
            CancellationToken.None);
        await third.Task.WaitAsync(Timeout);
        Assert.False(keeping.IsCompleted);

        var before = clock.Now;
        clock.Now = before + lease;
        var tried = Volatile.Read(ref failures);

        Assert.False(await keeping.WaitAsync(Timeout));
        Assert.InRange(failures, tried, tried + 1);
    }

    // On the real clock: a renewal that hangs is cut off at the deadline, so the lease is given up then and not a beat or a timeout later.
    [Fact]
    public async Task A_renewal_that_hangs_gives_the_lease_up_at_its_deadline()
    {
        var lease = TimeSpan.FromMilliseconds(600);
        var started = Stopwatch.StartNew();

        var kept = await JobQueueWorker.KeepLeaseAsync(
            async renewing =>
            {
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, renewing);
                return true;
            },
            DateTimeOffset.UtcNow + lease,
            lease,
            TimeProvider.System,
            NullLogger.Instance,
            CancellationToken.None).WaitAsync(Timeout);

        Assert.False(kept);
        Assert.True(started.Elapsed >= lease - TimeSpan.FromMilliseconds(50), $"Gave up after {started.Elapsed}, before the lease ran out.");
    }

    [Fact]
    public async Task A_refused_renewal_gives_the_lease_up_at_once()
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var lease = TimeSpan.FromMilliseconds(300);
        var calls = 0;

        var kept = await JobQueueWorker.KeepLeaseAsync(
            _ => Task.FromResult(Interlocked.Increment(ref calls) > 1),
            clock.Now + lease,
            lease,
            clock,
            NullLogger.Instance,
            CancellationToken.None).WaitAsync(Timeout);

        Assert.Equal((false, 1), (kept, calls));
    }

    // Once the handler is done, how the renewal in flight ended no longer matters, so it never stops the outcome being recorded.
    [Fact]
    public async Task A_renewal_that_fails_as_the_handler_finishes_counts_as_done()
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var lease = TimeSpan.FromMilliseconds(300);
        using var done = new CancellationTokenSource();

        var kept = await JobQueueWorker.KeepLeaseAsync(
            _ =>
            {
                done.Cancel();
                throw new TimeoutException("The database did not answer.");
            },
            clock.Now + lease,
            lease,
            clock,
            NullLogger.Instance,
            done.Token).WaitAsync(Timeout);

        Assert.True(kept);
    }

    [Fact]
    public void The_retry_delay_doubles_with_each_attempt_up_to_its_ceiling()
    {
        var settings = new JobQueueOptions { RetryDelay = TimeSpan.FromSeconds(30), MaxRetryDelay = TimeSpan.FromMinutes(3) };

        Assert.Equal(
            [TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120), TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3)],
            Enumerable.Range(1, 5).Select(attempt => JobQueueWorker.RetryDelay(settings, attempt)));
    }

    [Fact]
    public void The_retry_delay_stops_at_its_ceiling_however_many_attempts_there_were()
    {
        var settings = new JobQueueOptions { RetryDelay = TimeSpan.FromSeconds(30), MaxRetryDelay = TimeSpan.FromHours(1) };

        Assert.All(new[] { 36, 1_000, int.MaxValue }, attempt => Assert.Equal(TimeSpan.FromHours(1), JobQueueWorker.RetryDelay(settings, attempt)));
        Assert.Equal(TimeSpan.Zero, JobQueueWorker.RetryDelay(new JobQueueOptions { RetryDelay = TimeSpan.Zero }, int.MaxValue));
    }

    private static Dictionary<string, string?> Settings(TemporaryDatabase database, string lease = "00:00:01.500", int workers = 1)
    {
        return new Dictionary<string, string?>(database.Configuration.Concat(FastQueue))
        {
            ["JobQueue:LeaseDuration"] = lease,
            ["JobQueue:WorkerCount"] = workers.ToString(CultureInfo.InvariantCulture),
        };
    }

    private static void AddWaitingHandler(Microsoft.AspNetCore.Builder.WebApplicationBuilder builder)
    {
        builder.Services.AddFluentCmsExtension<IJobHandler, WaitingHandler>();
    }

    private static async Task<T> QueryAsync<T>(AdminHost host, Func<FluentCmsDbContext, Task<T>> query)
    {
        await using var scope = host.App.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>());
    }

    private static Task<JobRow> JobRowAsync(AdminHost host, Guid id)
    {
        return QueryAsync(host, context => context.Jobs.AsNoTracking().SingleAsync(row => row.Id == id));
    }

    private static async Task UntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not hold in time.");
            await Task.Delay(50);
        }
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

    // Waits until the test lets it finish or its token is cancelled, and records which it was.
    private sealed class WaitingHandler : IJobHandler
    {
        public const string Name = "test.waiting";

        private static readonly ConcurrentDictionary<Guid, Run> Runs = new();

        public string Type => Name;

        public static Run For(Guid jobId)
        {
            return Runs.GetOrAdd(jobId, _ => new Run());
        }

        public async Task<Result> RunAsync(JobContext context, CancellationToken cancellationToken)
        {
            var run = For(context.Job.Id);
            run.Count();
            run.Started.TrySetResult();
            try
            {
                await run.Finish.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                run.Cancelled.TrySetResult();
                throw;
            }

            return Result.Success();
        }

        public sealed class Run
        {
            private int _runs;

            public int Runs => _runs;

            public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public void Count()
            {
                Interlocked.Increment(ref _runs);
            }
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
