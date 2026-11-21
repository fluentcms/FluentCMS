using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using FluentCMS.Content;
using FluentCMS.Core.ActivityLog;
using FluentCMS.Core.Content;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Events;
using FluentCMS.Core.Idempotency;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.Members;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Telemetry;
using FluentCMS.Jobs;
using FluentCMS.Plugins;
using FluentCMS.Plugins.AiAssist;
using FluentCMS.Plugins.Webhooks;
using FluentCMS.Providers;
using FluentCMS.Sites;
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

internal sealed class ResumableJob(JobProbe probe) : IJob<ProbePayload>, IResumableJob
{
    public const string Type = "resumable";

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

internal sealed class ResumablePluginJob(JobProbe probe) : IJob<ProbePayload>
{
    public const string Type = "resumable-plugin.prune";

    public Task ExecuteAsync(ProbePayload payload, JobContext context)
    {
        probe.Runs.Enqueue(new JobRun(payload, context, null));
        return probe.Behavior(payload, context);
    }
}

// A plugin cannot see IResumableJob, so it says so when it registers the job.
[FluentCmsPlugin("resumable-plugin", SdkRange = "[1.0.0,2.0.0)")]
public sealed class ResumablePlugin : IPlugin
{
    public void ConfigureServices(IPluginServiceBuilder plugin) =>
        plugin.AddJob<ResumablePluginJob, ProbePayload>(ResumablePluginJob.Type, TimeSpan.FromSeconds(10), resumable: true);

    public void Configure(IPluginAppBuilder app)
    {
    }
}

// Ignores its token, as a sender that hangs does.
internal sealed class HangingEmailSender : IEmailSender
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Entered.TrySetResult();
        return Gate.Task;
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
    public async Task AJobThatIgnoresItsTokenIsAbandonedAfterItsLimitAndTheGraceAndFreesItsWorker()
    {
        using var abandoned = new AbandonedCount(TimedJob.Type);
        var gate = Hold();
        var instance = _fixture.NewInstance(cms: cms => cms.Jobs.AddJob<TimedJob, ProbePayload>(TimedJob.Type, Limit));
        var executor = instance.GetRequiredService<JobExecutor>();
        var id = await instance.GetRequiredService<IJobScheduler>().EnqueueAsync<TimedJob, ProbePayload>(new ProbePayload("stuck"), new JobOptions { MaxAttempts = 3 }, Token);

        var stuck = StartAsync(executor);
        await WaitForRunsAsync(1);
        var token = _fixture.Probe.Runs.Single().Context.CancellationToken;

        _fixture.Clock.Advance(Limit + JobExecutor.AbandonGrace - TimeSpan.FromTicks(1));
        token.IsCancellationRequested.ShouldBeTrue();
        _fixture.Job(id).Status.ShouldBe(JobStatus.Running, "the grace has not passed yet");
        _fixture.ShouldHaveRecorded(id, (1, null));
        executor.Abandoned.ShouldBe(0);

        _fixture.Clock.Advance(TimeSpan.FromTicks(1));
        (await stuck).ShouldBeTrue("the worker is free while the job still runs");

        var job = _fixture.Job(id);
        job.Status.ShouldBe(JobStatus.Pending);
        job.LastError.ShouldNotBeNull().ShouldStartWith("FCMS2508");
        job.LeaseOwner.ShouldBeNull();
        _fixture.ShouldHaveRecorded(id, (1, JobAttemptOutcome.Failed));
        _fixture.Logs.Count(nameof(JobLog.Abandoned)).ShouldBe(1);
        abandoned.Count.ShouldBe(1);
        executor.Abandoned.ShouldBe(1);

        // The abandoned job still holds a token that can be read, and its end writes nothing to the job.
        token.IsCancellationRequested.ShouldBeTrue();
        gate.SetResult();
        await Eventually.HoldsAsync(() => executor.Abandoned == 0);

        _fixture.ShouldHaveRecorded(id, (1, JobAttemptOutcome.Failed));
        _fixture.Job(id).Status.ShouldBe(JobStatus.Pending);
        _fixture.Logs.Count(nameof(JobLog.LeaseLost)).ShouldBe(0);
    }

    [Fact]
    public async Task TheGaugeOfAbandonedJobsCountsTheOnesStillRunning()
    {
        using var running = new AbandonedRunning();
        var gate = Hold();
        await _fixture.EnqueueAsync();
        var executor = Worker(Limit);

        // The gauge is the process's, so it is read against what other tests left running.
        var before = running.Read();
        var stuck = StartAsync(executor);
        await WaitForRunsAsync(1);
        _fixture.Clock.Advance(Limit + JobExecutor.AbandonGrace);
        await stuck;
        await Eventually.HoldsAsync(() => running.Read() == before + 1);

        gate.SetResult();
        await Eventually.HoldsAsync(() => executor.Abandoned == 0 && running.Read() == before);
    }

    [Fact]
    public async Task AJobThatEndsAtTheInstantOfItsCutOffKeepsItsOwnOutcome()
    {
        using var abandoned = new AbandonedCount(TimedJob.Type);
        var instance = _fixture.NewInstance(cms: cms => cms.Jobs.AddJob<TimedJob, ProbePayload>(TimedJob.Type, Limit));
        var executor = instance.GetRequiredService<JobExecutor>();
        var id = await instance.GetRequiredService<IJobScheduler>().EnqueueAsync<TimedJob, ProbePayload>(new ProbePayload("late"), new JobOptions { MaxAttempts = 1 }, Token);

        // A timer made before the claim fires first of those due at the same instant, so the job returns as the cut-off fires.
        var gate = new TaskCompletionSource();
        _fixture.Probe.Behavior = (_, _) => gate.Task;
        using var end = _fixture.Clock.CreateTimer(_ => gate.TrySetResult(), null, Limit + JobExecutor.AbandonGrace, Timeout.InfiniteTimeSpan);

        var run = StartAsync(executor);
        await WaitForRunsAsync(1);
        _fixture.Clock.Advance(Limit + JobExecutor.AbandonGrace);
        (await run).ShouldBeTrue();

        _fixture.Job(id).Status.ShouldBe(JobStatus.Succeeded);
        _fixture.ShouldHaveRecorded(id, (1, JobAttemptOutcome.Succeeded));
        abandoned.Count.ShouldBe(0);
        executor.Abandoned.ShouldBe(0);
    }

    [Fact]
    public async Task WhatAnAbandonedJobThrowsWhenItEndsIsLogged()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fixture.Probe.Behavior = async (_, _) =>
        {
            await gate.Task;
            throw new InvalidOperationException("It broke late.");
        };
        await _fixture.EnqueueAsync();
        var executor = Worker(Limit);

        var stuck = StartAsync(executor);
        await WaitForRunsAsync(1);
        _fixture.Clock.Advance(Limit + JobExecutor.AbandonGrace);
        await stuck;

        gate.SetResult();
        await Eventually.HoldsAsync(() => executor.Abandoned == 0);
        _fixture.Logs.Count(nameof(JobLog.AbandonedJobFailed)).ShouldBe(1);
    }

    [Fact]
    public async Task PastTheLimitOfAbandonedJobsAJobThatIgnoresItsTokenKeepsItsWorker()
    {
        var gates = new ConcurrentDictionary<string, TaskCompletionSource>();
        _fixture.Probe.Behavior = (payload, _) => gates.GetOrAdd(payload.Key, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        var first = await _fixture.EnqueueAsync("first");
        _fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        var second = await _fixture.EnqueueAsync("second");
        var executor = _fixture.NewInstance(configure: options =>
        {
            options.AttemptTimeout = Limit;
            options.MaxAbandonedJobs = 1;
        }).GetRequiredService<JobExecutor>();

        var one = StartAsync(executor);
        await WaitForRunsAsync(1);
        _fixture.Clock.Advance(Limit + JobExecutor.AbandonGrace);
        await one;
        executor.Abandoned.ShouldBe(1);

        var two = StartAsync(executor, "B");
        await WaitForRunsAsync(2);
        _fixture.Clock.Advance(Limit + JobExecutor.AbandonGrace);
        await Eventually.HoldsAsync(() => _fixture.Logs.Count(nameof(JobLog.AbandonRefused)) == 1);

        _fixture.Job(second).Status.ShouldBe(JobStatus.Running, "the worker keeps it");
        executor.Abandoned.ShouldBe(1);

        gates["second"].SetResult();
        (await two).ShouldBeTrue();
        _fixture.Job(second).Status.ShouldBe(JobStatus.Succeeded, "it returned normally, late");

        gates["first"].SetResult();
        await Eventually.HoldsAsync(() => executor.Abandoned == 0);
        _fixture.Job(first).LastError.ShouldNotBeNull().ShouldStartWith("FCMS2508");
    }

    [Fact]
    public async Task AJobThatBlocksItsThreadIsAbandonedToo()
    {
        using var gate = new ManualResetEventSlim();
        _fixture.Probe.Behavior = (_, _) =>
        {
            gate.Wait(Token);
            return Task.CompletedTask;
        };
        var id = await _fixture.EnqueueAsync(options: new JobOptions { MaxAttempts = 1 });

        var stuck = StartAsync(Worker(Limit));
        await WaitForRunsAsync(1);
        _fixture.Clock.Advance(Limit + JobExecutor.AbandonGrace);

        (await stuck).ShouldBeTrue();
        gate.Set();

        var job = _fixture.Job(id);
        job.Status.ShouldBe(JobStatus.DeadLettered, "it was the last attempt");
        job.LastError.ShouldNotBeNull().ShouldStartWith("FCMS2508");
        _fixture.Events.DeadLettered.Count.ShouldBe(1);
    }

    [Fact]
    public async Task AResumableJobStoppedAtItsLimitHasSucceededAndIsNeverDeadLettered()
    {
        var instance = _fixture.NewInstance(cms: cms => cms.Jobs.AddJob<ResumableJob, ProbePayload>(ResumableJob.Type, Limit));
        var scheduler = instance.GetRequiredService<IJobScheduler>();
        var executor = instance.GetRequiredService<JobExecutor>();
        RunUntilCancelled();

        for (var run = 1; run <= 3; run++)
        {
            var id = await scheduler.EnqueueAsync<ResumableJob, ProbePayload>(new ProbePayload($"run-{run}"), new JobOptions { MaxAttempts = 1 }, Token);
            var running = StartAsync(executor);
            await WaitForRunsAsync(run);
            _fixture.Clock.Advance(Limit);
            await running;

            var job = _fixture.Job(id);
            job.Status.ShouldBe(JobStatus.Succeeded, "what is left waits for the next run");
            job.LastError.ShouldBeNull();
            _fixture.ShouldHaveRecorded(id, (1, JobAttemptOutcome.Succeeded));
        }

        _fixture.Events.DeadLettered.ShouldBeEmpty();
        _fixture.Events.Failed.ShouldBeEmpty();
        _fixture.Logs.Count(nameof(JobLog.StoppedAtLimit)).ShouldBe(3);
    }

    [Fact]
    public void TheBuiltInJobsThatDeleteInBatchesAreResumable()
    {
        var resumable = _fixture.Services.GetServices<JobRegistration>()
            .Where(registration => registration.Resumable)
            .Select(registration => registration.Type)
            .Order(StringComparer.Ordinal);

        string[] expected = [ActivityRetentionJob.Type, TrashPurgeJob.Type, IdempotencyRetentionJob.Type, UnverifiedMembersJob.Type, OidcPruneJob.Type, JobRetentionJob.Type];
        resumable.ShouldBe(expected.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AJobThatEndsOnItsTokenWithinTheGraceIsNotAbandoned()
    {
        using var abandoned = new AbandonedCount(JobFixture.ProbeType);
        _fixture.Probe.Behavior = async (_, context) =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, context.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                await Task.Yield();
                throw;
            }
        };
        var id = await _fixture.EnqueueAsync();

        var running = StartAsync(Worker(Limit));
        await WaitForRunsAsync(1);
        _fixture.Clock.Advance(Limit);
        await running;

        _fixture.Job(id).LastError.ShouldNotBeNull().ShouldStartWith("FCMS2508");
        _fixture.Logs.Count(nameof(JobLog.Abandoned)).ShouldBe(0);
        abandoned.Count.ShouldBe(0);
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

    // The outbox drain alone runs without a limit: it cuts off each delivery itself, and runs for as long as rows are due. Any
    // other job a host registers by default must set an attempt timeout (#3408).
    [Fact]
    public void EveryBuiltInJobButTheseHasAnAttemptTimeout()
    {
        var untimed = _fixture.Services.GetServices<JobRegistration>()
            .Where(registration => registration.AttemptTimeout is null && registration.Type != JobFixture.ProbeType)
            .Select(registration => registration.Type)
            .Order(StringComparer.Ordinal);

        untimed.ShouldBe([OutboxDrainJob.Type]);
    }

    // The mail jobs had no limit, so a sender that hung held a worker of the mail queue for good (#3408).
    [Fact]
    public async Task ABuiltInJobThatIgnoresItsTokenIsAbandonedAtItsTimeoutAndFreesItsWorker()
    {
        var sender = new HangingEmailSender();
        var instance = _fixture.NewInstance(cms: cms => cms.Services.AddSingleton<IEmailSender>(sender));
        using (var scope = instance.CreateScope())
        {
            var services = scope.ServiceProvider;
            using var system = SystemOperation.Begin(services.GetRequiredService<IOperationContextAccessor>(), _fixture.Clock, Origin.Seed, new SiteId(DefaultSite.Id));
            (await services.GetRequiredService<CmsUserManager>().CreateAsync(new CmsUser { UserName = "hung", Email = "hung@example.com" })).Succeeded.ShouldBeTrue();
            await services.GetRequiredService<PasswordResetService>().RequestAsync("hung@example.com", new Uri("https://cms.example/reset"), Token);
        }

        var id = _fixture.Jobs().Single(job => job.Type == PasswordResetMailJob.Type).Id;
        var executor = instance.GetRequiredService<JobExecutor>();
        var running = Task.Run(() => executor.RunNextAsync(AccountMail.Queue, "A", Token), Token);
        await sender.Entered.Task.WaitAsync(Token);

        _fixture.Clock.Advance(AccountMail.AttemptTimeout + JobExecutor.AbandonGrace - TimeSpan.FromTicks(1));
        _fixture.Job(id).Status.ShouldBe(JobStatus.Running, "the grace has not passed yet");

        _fixture.Clock.Advance(TimeSpan.FromTicks(1));
        (await running).ShouldBeTrue("the worker is free while the send still hangs");

        var job = _fixture.Job(id);
        job.Status.ShouldBe(JobStatus.Pending);
        job.LastError.ShouldNotBeNull().ShouldStartWith("FCMS2508");
        executor.Abandoned.ShouldBe(1);

        sender.Gate.SetResult();
        await Eventually.HoldsAsync(() => executor.Abandoned == 0);
    }

    [Fact]
    public async Task APluginJobRegisteredResumableStoppedAtItsLimitHasSucceeded()
    {
        var instance = _fixture.NewInstance(cms: cms => cms.AddPlugin<ResumablePlugin>());
        var executor = instance.GetRequiredService<JobExecutor>();
        RunUntilCancelled();
        var id = await instance.GetRequiredService<IJobScheduler>().EnqueueAsync<ResumablePluginJob, ProbePayload>(new ProbePayload("plugin"), new JobOptions { MaxAttempts = 1 }, Token);

        var running = StartAsync(executor);
        await WaitForRunsAsync(1);
        _fixture.Clock.Advance(Limit);
        await running;

        var job = _fixture.Job(id);
        job.Status.ShouldBe(JobStatus.Succeeded, "what is left waits for the next run");
        job.LastError.ShouldBeNull();
        _fixture.Events.DeadLettered.ShouldBeEmpty();
        _fixture.Logs.Count(nameof(JobLog.StoppedAtLimit)).ShouldBe(1);
    }

    [Fact]
    public void TheRetentionJobsOfTheWebhooksAndAiAssistPluginsAreResumable()
    {
        var instance = _fixture.NewInstance(cms: cms =>
        {
            cms.AddPlugin<WebhooksPlugin>();
            cms.AddPlugin<AiAssistPlugin>();
        });
        var registry = instance.GetRequiredService<JobRegistry>();

        // The AI Assist plugin's job is internal to it, so its stored type is named here.
        foreach (var type in new[] { WebhookRetentionJob.Type, "ai.usage-retention" })
        {
            var registration = registry.Find(type).ShouldNotBeNull(type);
            registration.Resumable.ShouldBeTrue(type);
            registration.AttemptTimeout.ShouldNotBeNull(type);
        }
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

// Counts what the runner reports to fluentcms.job.abandoned for one job type, so that tests of other types running at the
// same time do not count.
internal sealed class AbandonedCount : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentQueue<long> _measurements = new();

    public AbandonedCount(string type)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == FluentCmsTelemetry.Name && instrument.Name == FluentCmsTelemetry.Instruments.JobsAbandoned)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == FluentCmsTelemetry.Tags.Operation && Equals(tag.Value, type))
                {
                    _measurements.Enqueue(value);
                }
            }
        });
        _listener.Start();
    }

    public long Count => _measurements.Sum();

    public void Dispose() => _listener.Dispose();
}

// Reads fluentcms.job.abandoned_running, which counts the abandoned invocations of the whole process.
internal sealed class AbandonedRunning : IDisposable
{
    private readonly MeterListener _listener = new();
    private long _value;

    public AbandonedRunning()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == FluentCmsTelemetry.Name && instrument.Name == FluentCmsTelemetry.Instruments.JobsAbandonedRunning)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Exchange(ref _value, value));
        _listener.Start();
    }

    public long Read()
    {
        _listener.RecordObservableInstruments();
        return Interlocked.Read(ref _value);
    }

    public void Dispose() => _listener.Dispose();
}
