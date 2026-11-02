using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Storage;
using FluentCMS.Data.Sqlite;
using FluentCMS.Data.Tests.Sqlite;
using FluentCMS.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Jobs;

// The real loops on real threads. Tests that race use the system clock with short polls and check only what must hold
// however the threads fall; tests about time use the manual clock.
public sealed class JobRunnerTests : IDisposable
{
    private static readonly TimeSpan QuickPoll = TimeSpan.FromMilliseconds(5);

    private readonly JobFixture _fixture = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _fixture.Dispose();

    private ServiceProvider QuickInstance(int workers = 4) =>
        _fixture.NewInstance(TimeProvider.System, options =>
        {
            options.Queues[JobRunnerOptions.DefaultQueue] = workers;
            options.PollInterval = QuickPoll;
            options.MaintenanceInterval = QuickPoll;
        });

    private ServiceProvider ManualInstance() =>
        _fixture.NewInstance(configure: options => options.Queues[JobRunnerOptions.DefaultQueue] = 1);

    private void WakeTheLoops() => _fixture.Clock.Advance(new JobRunnerOptions().PollInterval);

    [Fact]
    public async Task AHostStartsTheRunnerAfterMigratingAndStopsIt()
    {
        var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var database = new SqliteFileDatabase();
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Production" });
        var probe = new JobProbe { Behavior = (_, _) => { ran.SetResult(); return Task.CompletedTask; } };
        builder.Services.AddSingleton(probe);
        builder.Services.AddSingleton(new SchemaBuilder().Build());
        builder.Services.AddFluentCms(cms =>
        {
            cms.UseSqlite(database.ConnectionString);
            cms.UseContentMigrations(typeof(CmsDbContext).Assembly);
            cms.Jobs.AddJob<ProbeJob, ProbePayload>(JobFixture.ProbeType);
            cms.Jobs.Options.PollInterval = QuickPoll;
        });

        using var host = builder.Build();
        await host.StartAsync(Token);

        await host.Services.GetRequiredService<IJobScheduler>().EnqueueAsync<ProbeJob, ProbePayload>(new ProbePayload("a"), cancellationToken: Token);
        await ran.Task.WaitAsync(TimeSpan.FromSeconds(60), Token);

        await host.StopAsync(Token);
        probe.Runs.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("wal")]
    public async Task TwoRunnersOnOneDatabaseNeverRunAJobTwice(string journalMode)
    {
        _fixture.Database.Execute($"PRAGMA journal_mode = {journalMode}");

        const int Jobs = 60;
        var ids = new List<Guid>();
        for (var job = 0; job < Jobs / 2; job++)
        {
            ids.Add(await _fixture.EnqueueAsync($"before-{job}"));
        }

        var runners = new[] { QuickInstance(), QuickInstance() }.Select(instance => instance.GetRequiredService<IJobRunner>()).ToList();
        await Task.WhenAll(runners.Select(runner => runner.StartAsync(Token)));

        // Half arrive while the runners are claiming.
        for (var job = 0; job < Jobs / 2; job++)
        {
            ids.Add(await _fixture.EnqueueAsync($"during-{job}"));
        }

        await Eventually.HoldsAsync(() => _fixture.Jobs().Count(job => job.Status == JobStatus.Succeeded) == Jobs);
        await Task.WhenAll(runners.Select(runner => runner.StopAsync(Token)));

        _fixture.Probe.Runs.Count.ShouldBe(Jobs);
        _fixture.Probe.Runs.Select(run => run.Payload.Key).ShouldBeUnique();
        foreach (var id in ids)
        {
            _fixture.Job(id).Attempts.ShouldBe(1);
            _fixture.ShouldHaveRecorded(id, (1, JobAttemptOutcome.Succeeded));
        }
    }

    [Fact]
    public async Task AFailingJobDoesNotTakeAWorkerDown()
    {
        _fixture.Probe.Behavior = (payload, _) => payload.Key == "bad" ? throw new InvalidOperationException("It broke.") : Task.CompletedTask;
        var bad = await _fixture.EnqueueAsync("bad", new JobOptions { MaxAttempts = 1, Priority = 1 });
        var unknown = _fixture.AddRow("unknown", "{}", maxAttempts: 1);
        var unreadable = _fixture.AddRow(JobFixture.ProbeType, """{"key":5}""", maxAttempts: 1);
        var good = await _fixture.EnqueueAsync("good");

        var runner = QuickInstance(workers: 1).GetRequiredService<IJobRunner>();
        await runner.StartAsync(Token);
        await Eventually.HoldsAsync(() => _fixture.Job(good).Status == JobStatus.Succeeded);
        await runner.StopAsync(Token);

        _fixture.Job(bad).Status.ShouldBe(JobStatus.DeadLettered);
        _fixture.Job(unknown).Status.ShouldBe(JobStatus.DeadLettered);
        _fixture.Job(unreadable).Status.ShouldBe(JobStatus.DeadLettered);
    }

    [Fact]
    public async Task EachQueueHasItsOwnWorkersUpToItsLimit()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = 0;
        var most = 0;
        _fixture.Probe.Behavior = async (_, _) =>
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref most, now);
            await gate.Task;
            Interlocked.Decrement(ref running);
        };

        for (var job = 0; job < 6; job++)
        {
            await _fixture.EnqueueAsync($"limited-{job}");
        }

        var runner = QuickInstance(workers: 2).GetRequiredService<IJobRunner>();
        await runner.StartAsync(Token);
        await Eventually.HoldsAsync(() => Volatile.Read(ref running) == 2);

        _fixture.Probe.Runs.Count.ShouldBe(2);

        gate.SetResult();
        await Eventually.HoldsAsync(() => _fixture.Jobs().All(job => job.Status == JobStatus.Succeeded));
        await runner.StopAsync(Token);

        most.ShouldBe(2);
    }

    [Fact]
    public async Task AHeartbeatKeepsALongJobsLease()
    {
        var options = new JobRunnerOptions();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fixture.Probe.Behavior = (_, _) => gate.Task;
        var id = await _fixture.EnqueueAsync();

        var runner = ManualInstance().GetRequiredService<IJobRunner>();
        await runner.StartAsync(Token);
        await Eventually.HoldsAsync(() => !_fixture.Probe.Runs.IsEmpty);

        // The job outlives its first lease several times over.
        var beats = (int)(options.LeaseDuration / options.HeartbeatInterval) * 3;
        for (var beat = 1; beat <= beats; beat++)
        {
            _fixture.Clock.Advance(options.HeartbeatInterval);

            var expected = _fixture.Clock.GetUtcNow() + options.LeaseDuration;
            await Eventually.HoldsAsync(() => _fixture.Job(id).LeaseUntil == expected);

            (await _fixture.Store.ClaimAsync(JobRunnerOptions.DefaultQueue, "thief")).ShouldBeNull();
            (await _fixture.Store.SweepAsync()).ShouldBe(0);
        }

        gate.SetResult();
        await Eventually.HoldsAsync(() => _fixture.Job(id).Status == JobStatus.Succeeded);
        await StopAsync(runner);

        _fixture.Probe.Runs.Count.ShouldBe(1);
        _fixture.Job(id).Attempts.ShouldBe(1);
    }

    [Fact]
    public async Task AJobWithoutAHeartbeatLosesItsLease()
    {
        var options = new JobRunnerOptions();
        var id = await _fixture.EnqueueAsync();

        await _fixture.Store.ClaimAsync(JobRunnerOptions.DefaultQueue, "silent");
        _fixture.Clock.Advance(options.LeaseDuration + TimeSpan.FromTicks(1));

        (await _fixture.Store.ClaimAsync(JobRunnerOptions.DefaultQueue, "next")).ShouldNotBeNull().Id.ShouldBe(id);
    }

    [Fact]
    public async Task ShutdownLetsARunningJobFinishAndTakesNoMore()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fixture.Probe.Behavior = (_, _) => gate.Task;
        var running = await _fixture.EnqueueAsync("running");

        var runner = ManualInstance().GetRequiredService<IJobRunner>();
        await runner.StartAsync(Token);
        await Eventually.HoldsAsync(() => !_fixture.Probe.Runs.IsEmpty);

        var waiting = await _fixture.EnqueueAsync("waiting");
        var stopping = runner.StopAsync(Token);

        stopping.IsCompleted.ShouldBeFalse();

        gate.SetResult();
        await Eventually.CompletesAsync(stopping);

        _fixture.Job(running).Status.ShouldBe(JobStatus.Succeeded);
        _fixture.Job(waiting).Status.ShouldBe(JobStatus.Pending);
        _fixture.Probe.RunsOf("waiting").ShouldBe(0);
    }

    [Fact]
    public async Task AJobThatOutlastsTheGracePeriodIsReleasedAndRunsAgainWithoutLosingAnAttempt()
    {
        _fixture.Probe.Behavior = (_, context) => Task.Delay(Timeout.Infinite, context.CancellationToken);
        var id = await _fixture.EnqueueAsync(options: new JobOptions { MaxAttempts = 1 });

        // The clock moves on while the release is written, however long that takes, so the runner must not give up on it.
        var runner = _fixture.NewInstance(configure: options =>
        {
            options.Queues[JobRunnerOptions.DefaultQueue] = 1;
            options.ReleaseTimeout = TimeSpan.FromDays(30);
        }).GetRequiredService<IJobRunner>();
        await runner.StartAsync(Token);
        await Eventually.HoldsAsync(() => !_fixture.Probe.Runs.IsEmpty);

        await Eventually.CompletesAsync(runner.StopAsync(Token), WakeTheLoops);

        var released = _fixture.Job(id);
        released.Status.ShouldBe(JobStatus.Pending);
        released.Attempts.ShouldBe(1);
        released.MaxAttempts.ShouldBe(2);
        released.LeaseOwner.ShouldBeNull();
        released.LeaseUntil.ShouldBeNull();
        released.LastError.ShouldBeNull();
        _fixture.ShouldHaveRecorded(id, (1, JobAttemptOutcome.Released));
        _fixture.Events.Failed.ShouldBeEmpty();

        _fixture.Probe.Behavior = (_, _) => Task.CompletedTask;
        (await _fixture.Executor.RunNextAsync(JobRunnerOptions.DefaultQueue, "after-restart", Token)).ShouldBeTrue();

        _fixture.Job(id).Status.ShouldBe(JobStatus.Succeeded);
        _fixture.ShouldHaveRecorded(id, (1, JobAttemptOutcome.Released), (2, JobAttemptOutcome.Succeeded));
    }

    [Fact]
    public async Task AHostOutOfPatienceCutsTheGracePeriodShort()
    {
        _fixture.Probe.Behavior = (_, context) => Task.Delay(Timeout.Infinite, context.CancellationToken);
        var id = await _fixture.EnqueueAsync();

        var runner = ManualInstance().GetRequiredService<IJobRunner>();
        await runner.StartAsync(Token);
        await Eventually.HoldsAsync(() => !_fixture.Probe.Runs.IsEmpty);

        await Eventually.CompletesAsync(runner.StopAsync(new CancellationToken(canceled: true)));

        _fixture.Job(id).Status.ShouldBe(JobStatus.Pending);
    }

    [Fact]
    public async Task AJobThatIgnoresCancellationDoesNotHoldUpTheStop()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fixture.Probe.Behavior = (_, _) => gate.Task;
        var id = await _fixture.EnqueueAsync();

        var runner = ManualInstance().GetRequiredService<IJobRunner>();
        await runner.StartAsync(Token);
        await Eventually.HoldsAsync(() => !_fixture.Probe.Runs.IsEmpty);

        await Eventually.CompletesAsync(runner.StopAsync(Token), WakeTheLoops);

        // Its heartbeat has stopped, so another instance can take the job once the lease has run out. A live heartbeat
        // would renew the lease as the clock moves, and this would never get the job. A last renewal can still be on
        // its way, which is why the clock moves more than once.
        ClaimedJob? taken = null;
        for (var tries = 0; taken is null && tries < 20; tries++)
        {
            _fixture.Clock.Advance(new JobRunnerOptions().LeaseDuration + TimeSpan.FromTicks(1));
            taken = await _fixture.Store.ClaimAsync(JobRunnerOptions.DefaultQueue, "next");
        }

        taken.ShouldNotBeNull().Id.ShouldBe(id);

        gate.SetResult();
    }

    [Fact]
    public async Task StoppingWithoutStartingDoesNothing() =>
        await ManualInstance().GetRequiredService<IJobRunner>().StopAsync(Token);

    [Fact]
    public async Task BadOptionsStopTheStart()
    {
        var runner = _fixture.NewInstance(configure: options => options.HeartbeatInterval = options.LeaseDuration).GetRequiredService<IJobRunner>();

        (await Should.ThrowAsync<FluentCmsConfigurationException>(() => runner.StartAsync(Token))).Code.ShouldBe("FCMS2506");
    }

    [Fact]
    public async Task ARunnerSweepsAndFiresOnItsOwn()
    {
        await _fixture.Scheduler.AddRecurringAsync<ProbeJob, ProbePayload>("r", "* * * * *", new ProbePayload("recurring"), cancellationToken: Token);
        var stuck = await _fixture.EnqueueAsync("stuck", new JobOptions { MaxAttempts = 1, Queue = "no-workers" });
        await _fixture.Store.ClaimAsync("no-workers", "dead");

        var options = new JobRunnerOptions();
        var runner = ManualInstance().GetRequiredService<IJobRunner>();
        await runner.StartAsync(Token);

        await Eventually.HoldsAsync(
            () => _fixture.Probe.RunsOf("recurring") >= 1 && _fixture.Job(stuck).Status == JobStatus.DeadLettered,
            () => _fixture.Clock.Advance(options.MaintenanceInterval));

        await Eventually.CompletesAsync(runner.StopAsync(Token), WakeTheLoops);
    }

    [Fact]
    public async Task AReplacementRunnerIsWhatTheHostStarts()
    {
        using var database = new SqliteFileDatabase();
        var replacement = new ReplacementRunner();
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Production" });
        builder.Services.AddSingleton<IJobRunner>(replacement);
        builder.Services.AddSingleton(new SchemaBuilder().Build());
        builder.Services.AddFluentCms(cms =>
        {
            cms.UseSqlite(database.ConnectionString);
            cms.UseContentMigrations(typeof(CmsDbContext).Assembly);
        });

        using var host = builder.Build();
        await host.StartAsync(Token);
        await host.StopAsync(Token);

        replacement.Started.ShouldBeTrue();
        replacement.Stopped.ShouldBeTrue();
    }

    private async Task StopAsync(IJobRunner runner) => await Eventually.CompletesAsync(runner.StopAsync(Token), WakeTheLoops);

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while (value > (seen = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, seen) != seen)
        {
        }
    }

    private sealed class ReplacementRunner : IJobRunner
    {
        public bool Started { get; private set; }

        public bool Stopped { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            Started = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            Stopped = true;
            return Task.CompletedTask;
        }
    }
}
