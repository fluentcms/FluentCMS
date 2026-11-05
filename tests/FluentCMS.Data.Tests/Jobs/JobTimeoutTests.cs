using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Jobs;
using FluentCMS.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Jobs;

internal sealed class TimedJob(JobProbe probe) : IJob<ProbePayload>
{
    public const string Type = "timed";

    public Task ExecuteAsync(ProbePayload payload, JobContext context)
    {
        probe.Runs.Enqueue(new JobRun(payload, context, null));
        return probe.Behavior(payload, context);
    }
}

internal sealed class UntimedJob(JobProbe probe) : IJob<ProbePayload>
{
    public const string Type = "untimed";

    public Task ExecuteAsync(ProbePayload payload, JobContext context)
    {
        probe.Runs.Enqueue(new JobRun(payload, context, null));
        return probe.Behavior(payload, context);
    }
}

public sealed class JobTimeoutTests : IDisposable
{
    private const string Queue = JobRunnerOptions.DefaultQueue;

    private static readonly JobRunnerOptions Defaults = new();
    private static readonly TimeSpan Beat = Defaults.HeartbeatInterval;
    private static readonly TimeSpan Lease = Defaults.LeaseDuration;
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    private readonly JobFixture _fixture = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _fixture.Dispose();

    private JobExecutor Worker(TimeSpan? timeout = null) =>
        _fixture.NewInstance(configure: options => options.AttemptTimeout = timeout).GetRequiredService<JobExecutor>();

    private static Task<bool> StartAsync(JobExecutor worker, string owner = "A") => Task.Run(() => worker.RunNextAsync(Queue, owner, Token), Token);

    private Task WaitForRunsAsync(int runs) => Eventually.HoldsAsync(() => _fixture.Probe.Runs.Count >= runs);

    private TaskCompletionSource Hold()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fixture.Probe.Behavior = (_, _) => gate.Task;
        return gate;
    }

    private void RunUntilCancelled() =>
        _fixture.Probe.Behavior = (_, context) => Task.Delay(Timeout.Infinite, context.CancellationToken);

    [Fact]
    public async Task AnAttemptPastItsTimeoutIsCancelledAndRetriedWithBackoff()
    {
        RunUntilCancelled();
        var id = await _fixture.EnqueueAsync(options: new JobOptions { MaxAttempts = 3 });

        var running = StartAsync(Worker(Limit));
        await WaitForRunsAsync(1);
        var attempt = _fixture.Probe.Runs.Single().Context.CancellationToken;

        _fixture.Clock.Advance(Limit - TimeSpan.FromTicks(1));
        attempt.IsCancellationRequested.ShouldBeFalse();

        _fixture.Clock.Advance(TimeSpan.FromTicks(1));
        (await running).ShouldBeTrue();

        attempt.IsCancellationRequested.ShouldBeTrue();
        var job = _fixture.Job(id);
        var now = _fixture.Clock.GetUtcNow();
        job.Status.ShouldBe(JobStatus.Pending);
        job.LastError.ShouldNotBeNull().ShouldStartWith("FCMS2508");
        job.RunAt.ShouldBeInRange(now.AddSeconds(30), now.AddSeconds(36));
        job.LeaseOwner.ShouldBeNull();
        job.LeaseUntil.ShouldBeNull();
        _fixture.ShouldHaveRecorded(id, (1, JobAttemptOutcome.Failed));
        _fixture.Attempts(id).Single().FinishedAt.ShouldBe(now);
        _fixture.Events.Failed.Single().Error.ShouldStartWith("FCMS2508");

        _fixture.Probe.Behavior = (_, _) => Task.CompletedTask;
        (await Worker().RunNextAsync(Queue, "A", Token)).ShouldBeFalse("the retry is not due yet");

        _fixture.Clock.Advance(job.RunAt - now);
        (await Worker().RunNextAsync(Queue, "A", Token)).ShouldBeTrue();

        _fixture.Job(id).Status.ShouldBe(JobStatus.Succeeded);
        _fixture.ShouldHaveRecorded(id, (1, JobAttemptOutcome.Failed), (2, JobAttemptOutcome.Succeeded));
    }

    [Fact]
    public async Task AnAttemptPastItsTimeoutOnTheLastAttemptIsDeadLettered()
    {
        RunUntilCancelled();
        var id = await _fixture.EnqueueAsync(options: new JobOptions { MaxAttempts = 1 });

        var running = StartAsync(Worker(Limit));
        await WaitForRunsAsync(1);
        _fixture.Clock.Advance(Limit);
        await running;

        var job = _fixture.Job(id);
        job.Status.ShouldBe(JobStatus.DeadLettered);
        job.LastError.ShouldNotBeNull().ShouldStartWith("FCMS2508");
        job.LeaseOwner.ShouldBeNull();
        job.LeaseUntil.ShouldBeNull();
        _fixture.Events.DeadLettered.Count.ShouldBe(1);
        _fixture.ShouldHaveRecorded(id, (1, JobAttemptOutcome.Failed));
    }

    [Fact]
    public async Task AJobThatHonoursItsTokenLeavesNoLeaseBehind()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fixture.Probe.Behavior = async (_, context) =>
        {
            if (context.Attempt == 1)
            {
                await Task.Delay(Timeout.Infinite, context.CancellationToken);
            }

            await gate.Task;
        };
        var id = await _fixture.EnqueueAsync();

        var first = StartAsync(Worker(Limit));
        await WaitForRunsAsync(1);
        _fixture.Clock.Advance(Limit);
        await first;

        _fixture.Job(id).LeaseUntil.ShouldBeNull();

        _fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = StartAsync(Worker(Limit), "B");
        await WaitForRunsAsync(2);

        var running = _fixture.Job(id);
        running.Status.ShouldBe(JobStatus.Running);
        running.LeaseOwner.ShouldBe("B");
        running.Attempts.ShouldBe(2);

        gate.SetResult();
        await second;
        _fixture.Job(id).Status.ShouldBe(JobStatus.Succeeded);
        _fixture.ShouldHaveRecorded(id, (1, JobAttemptOutcome.Failed), (2, JobAttemptOutcome.Succeeded));
    }

    [Fact]
    public async Task AJobThatIgnoresItsTokenStopsBeingRenewedAndIsTakenOverWhenItsLeaseLapses()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fixture.Probe.Behavior = async (_, context) =>
        {
            if (context.Attempt == 1)
            {
                await gate.Task;
            }
        };
        var id = await _fixture.EnqueueAsync();

        var stuck = StartAsync(Worker(Beat + Limit));
        await WaitForRunsAsync(1);

        _fixture.Clock.Advance(Beat);
        var renewed = _fixture.Clock.GetUtcNow() + Lease;
        await Eventually.HoldsAsync(() => _fixture.Job(id).LeaseUntil == renewed);

        _fixture.Clock.Advance(Limit);
        _fixture.Probe.Runs.Single().Context.CancellationToken.IsCancellationRequested.ShouldBeTrue();
        (await _fixture.Executor.RunNextAsync(Queue, "B", Token)).ShouldBeFalse("the lease has not lapsed yet");

        while (_fixture.Clock.GetUtcNow() <= renewed)
        {
            _fixture.Clock.Advance(Beat);
        }

        _fixture.Job(id).LeaseUntil.ShouldBe(renewed, "nothing renewed it after the timeout");

        (await _fixture.Executor.RunNextAsync(Queue, "B", Token)).ShouldBeTrue();
        _fixture.Job(id).Status.ShouldBe(JobStatus.Succeeded);

        gate.SetResult();
        (await stuck).ShouldBeTrue();

        var job = _fixture.Job(id);
        job.Status.ShouldBe(JobStatus.Succeeded);
        job.Attempts.ShouldBe(2);
        _fixture.ShouldHaveRecorded(id, (1, JobAttemptOutcome.LeaseExpired), (2, JobAttemptOutcome.Succeeded));
        _fixture.Logs.Count(nameof(JobLog.LeaseLost)).ShouldBe(1);
    }

    [Fact]
    public async Task AJobThatIgnoresItsTokenOnItsLastAttemptIsDeadLetteredBySweeping()
    {
        var gate = Hold();
        var id = await _fixture.EnqueueAsync(options: new JobOptions { MaxAttempts = 1 });

        var stuck = StartAsync(Worker(Limit));
        await WaitForRunsAsync(1);
        _fixture.Clock.Advance(Limit);

        (await _fixture.Store.SweepAsync()).ShouldBe(0);
        _fixture.Clock.Advance(Lease);
        (await _fixture.Store.SweepAsync()).ShouldBe(1);
        _fixture.Job(id).Status.ShouldBe(JobStatus.DeadLettered);

        gate.SetResult();
        await stuck;

        _fixture.Job(id).Status.ShouldBe(JobStatus.DeadLettered);
        _fixture.Logs.Count(nameof(JobLog.LeaseLost)).ShouldBe(1);
    }

    [Fact]
    public async Task AJobThatFinishesAfterItsTimeoutWithoutBeingCancelledHasSucceeded()
    {
        var gate = Hold();
        var id = await _fixture.EnqueueAsync();

        var running = StartAsync(Worker(Limit));
        await WaitForRunsAsync(1);
        _fixture.Clock.Advance(Limit);

        gate.SetResult();
        await running;

        _fixture.Job(id).Status.ShouldBe(JobStatus.Succeeded);
        _fixture.ShouldHaveRecorded(id, (1, JobAttemptOutcome.Succeeded));
    }

    [Fact]
    public async Task ThereIsNoTimeoutUnlessOneIsSet()
    {
        var gate = Hold();
        var id = await _fixture.EnqueueAsync();

        var running = StartAsync(Worker());
        await WaitForRunsAsync(1);
        var attempt = _fixture.Probe.Runs.Single().Context.CancellationToken;

        for (var beat = 0; beat < 100; beat++)
        {
            _fixture.Clock.Advance(Beat);
            var expected = _fixture.Clock.GetUtcNow() + Lease;
            await Eventually.HoldsAsync(() => _fixture.Job(id).LeaseUntil == expected);
        }

        attempt.IsCancellationRequested.ShouldBeFalse();
        _fixture.Job(id).Status.ShouldBe(JobStatus.Running);

        gate.SetResult();
        await running;
        _fixture.Job(id).Status.ShouldBe(JobStatus.Succeeded);
    }

    [Fact]
    public async Task ARegistrationTimeoutOverridesTheDefaultAndInfiniteTurnsItOff()
    {
        var gate = Hold();
        var instance = _fixture.NewInstance(
            configure: options => options.AttemptTimeout = TimeSpan.FromHours(1),
            cms: cms => cms.Jobs
                .AddJob<TimedJob, ProbePayload>(TimedJob.Type, Limit)
                .AddJob<UntimedJob, ProbePayload>(UntimedJob.Type, Timeout.InfiniteTimeSpan));
        var worker = instance.GetRequiredService<JobExecutor>();
        var scheduler = instance.GetRequiredService<IJobScheduler>();

        var timed = await scheduler.EnqueueAsync<TimedJob, ProbePayload>(new ProbePayload("timed"), cancellationToken: Token);
        var running = StartAsync(worker);
        await WaitForRunsAsync(1);
        var timedAttempt = _fixture.Probe.Runs.Single().Context.CancellationToken;
        _fixture.Clock.Advance(Limit);
        timedAttempt.IsCancellationRequested.ShouldBeTrue("the registration's own timeout applies, not the hour");
        gate.SetResult();
        await running;

        var untimed = await scheduler.EnqueueAsync<UntimedJob, ProbePayload>(new ProbePayload("untimed"), cancellationToken: Token);
        gate = Hold();
        running = StartAsync(worker);
        await WaitForRunsAsync(2);
        var untimedAttempt = _fixture.Probe.Runs.Last().Context.CancellationToken;
        _fixture.Clock.Advance(TimeSpan.FromHours(2));

        untimedAttempt.IsCancellationRequested.ShouldBeFalse("infinite turns the default off");
        _fixture.Job(untimed).Status.ShouldBe(JobStatus.Running);

        gate.SetResult();
        await running;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ANonPositiveTimeoutIsRefused(int seconds)
    {
        var timeout = TimeSpan.FromSeconds(seconds);

        var refused = Should.Throw<ArgumentOutOfRangeException>(() =>
            _fixture.NewInstance(cms: cms => cms.Jobs.AddJob<TimedJob, ProbePayload>(TimedJob.Type, timeout)));
        refused.ParamName.ShouldBe("attemptTimeout");

        var services = _fixture.NewInstance(configure: options => options.AttemptTimeout = timeout);
        Should.Throw<FluentCmsConfigurationException>(() => services.GetRequiredService<IJobRunner>()).Code.ShouldBe("FCMS2506");
    }
}
