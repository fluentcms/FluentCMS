using System.Data.Common;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Storage;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Jobs;

// What happens when an instance loses the database, or a job's row is not what it should be.
public sealed class JobFailureTests : IDisposable
{
    private const string Queue = JobRunnerOptions.DefaultQueue;

    private static readonly JobRunnerOptions Defaults = new();
    private static readonly TimeSpan Beat = Defaults.HeartbeatInterval;
    private static readonly TimeSpan Lease = Defaults.LeaseDuration;

    private readonly JobFixture _fixture = new();
    private readonly JobFaults _faults = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _fixture.Dispose();

    private JobExecutor FaultyExecutor() => _fixture.NewInstance(faults: _faults).GetRequiredService<JobExecutor>();

    private void RunUntilCancelled(Action<CancellationToken>? started = null) =>
        _fixture.Probe.Behavior = async (_, context) =>
        {
            started?.Invoke(context.CancellationToken);
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        };

    private async Task BeatAsync(int beats)
    {
        var before = _fixture.Logs.Count(nameof(JobLog.HeartbeatFailed));
        for (var beat = 1; beat <= beats; beat++)
        {
            _fixture.Clock.Advance(Beat);
            await Eventually.HoldsAsync(() => _fixture.Logs.Count(nameof(JobLog.HeartbeatFailed)) == before + beat);
        }
    }

    [Fact]
    public async Task AWorkerThatCannotRenewStopsBeforeItsLeaseEnds()
    {
        var attempt = CancellationToken.None;
        RunUntilCancelled(token => attempt = token);
        var id = await _fixture.EnqueueAsync();

        var running = Task.Run(() => FaultyExecutor().RunNextAsync(Queue, "A", Token), Token);
        await Eventually.HoldsAsync(() => !_fixture.Probe.Runs.IsEmpty);
        _faults.FailEverything();

        // A renewal is still possible in time up to here.
        await BeatAsync(3);
        attempt.IsCancellationRequested.ShouldBeFalse();

        // The next would come only as the lease ends.
        await BeatAsync(1);
        (await running).ShouldBeTrue();
        attempt.IsCancellationRequested.ShouldBeTrue();
        (_fixture.Job(id).LeaseUntil > _fixture.Clock.GetUtcNow()).ShouldBeTrue("it stopped while nobody else could have the job");
        _fixture.Job(id).Status.ShouldBe(JobStatus.Running);

        (await _fixture.Executor.RunNextAsync(Queue, "B", Token)).ShouldBeFalse();

        _fixture.Probe.Behavior = (_, _) => Task.CompletedTask;
        _fixture.Clock.Advance(Beat + TimeSpan.FromTicks(1));
        (await _fixture.Executor.RunNextAsync(Queue, "B", Token)).ShouldBeTrue();

        _fixture.Job(id).Status.ShouldBe(JobStatus.Succeeded);
        _fixture.ShouldHaveRecorded(id, (1, JobAttemptOutcome.LeaseExpired), (2, JobAttemptOutcome.Succeeded));
    }

    [Fact]
    public async Task OnTheLastAttemptTheSweepOnlyTakesAJobAlreadyCancelled()
    {
        var attempt = CancellationToken.None;
        RunUntilCancelled(token => attempt = token);
        var id = await _fixture.EnqueueAsync(options: new JobOptions { MaxAttempts = 1 });

        var running = Task.Run(() => FaultyExecutor().RunNextAsync(Queue, "A", Token), Token);
        await Eventually.HoldsAsync(() => !_fixture.Probe.Runs.IsEmpty);
        _faults.FailEverything();

        await BeatAsync(4);
        await running;
        attempt.IsCancellationRequested.ShouldBeTrue();

        _fixture.Clock.Advance(Beat + TimeSpan.FromTicks(1));
        (await _fixture.Store.SweepAsync()).ShouldBe(1);

        _fixture.Job(id).Status.ShouldBe(JobStatus.DeadLettered);
    }

    [Fact]
    public async Task AHeartbeatThatRecoversInTimeKeepsTheJob()
    {
        var attempt = CancellationToken.None;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fixture.Probe.Behavior = async (_, context) =>
        {
            attempt = context.CancellationToken;
            await gate.Task;
        };
        var id = await _fixture.EnqueueAsync();

        var running = Task.Run(() => FaultyExecutor().RunNextAsync(Queue, "A", Token), Token);
        await Eventually.HoldsAsync(() => !_fixture.Probe.Runs.IsEmpty);

        _faults.FailEverything();
        await BeatAsync(3);
        _faults.Recover();

        // Enough beats that only the renewals after the outage can have kept the lease.
        for (var beat = 0; beat < 10; beat++)
        {
            _fixture.Clock.Advance(Beat);
            var expected = _fixture.Clock.GetUtcNow() + Lease;
            await Eventually.HoldsAsync(() => _fixture.Job(id).LeaseUntil == expected);
        }

        attempt.IsCancellationRequested.ShouldBeFalse();
        gate.SetResult();
        (await running).ShouldBeTrue();

        _fixture.Job(id).Status.ShouldBe(JobStatus.Succeeded);
        _fixture.Job(id).Attempts.ShouldBe(1);
    }

    [Fact]
    public async Task ARenewalThatIsCancelledDoesNotEndTheHeartbeat()
    {
        var attempt = CancellationToken.None;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fixture.Probe.Behavior = async (_, context) =>
        {
            attempt = context.CancellationToken;
            await gate.Task;
        };
        var id = await _fixture.EnqueueAsync();

        var running = Task.Run(() => FaultyExecutor().RunNextAsync(Queue, "A", Token), Token);
        await Eventually.HoldsAsync(() => !_fixture.Probe.Runs.IsEmpty);

        _faults.Fail(command =>
        {
            if (!command.CommandText.Contains(TestProvider.Quote("lease_until"), StringComparison.Ordinal))
            {
                return false;
            }

            _faults.Recover();
            throw new OperationCanceledException();
        });
        await BeatAsync(1);

        // The next renewal still happens, so the lease outlives what it would have been without one.
        for (var beat = 0; beat < 6; beat++)
        {
            _fixture.Clock.Advance(Beat);
            var expected = _fixture.Clock.GetUtcNow() + Lease;
            await Eventually.HoldsAsync(() => _fixture.Job(id).LeaseUntil == expected);
        }

        attempt.IsCancellationRequested.ShouldBeFalse();
        (await _fixture.Store.ClaimAsync(Queue, "B", Token)).ShouldBeNull();

        gate.SetResult();
        (await running).ShouldBeTrue();

        _fixture.Job(id).Status.ShouldBe(JobStatus.Succeeded);
        _fixture.Job(id).Attempts.ShouldBe(1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task ABriefOutageDoesNotLoseASuccess(int maxAttempts)
    {
        _fixture.Probe.Behavior = (_, _) =>
        {
            _faults.FailEverything();
            return Task.CompletedTask;
        };
        var id = await _fixture.EnqueueAsync(options: new JobOptions { MaxAttempts = maxAttempts });

        var running = Task.Run(() => FaultyExecutor().RunNextAsync(Queue, "A", Token), Token);
        await Eventually.HoldsAsync(() => _fixture.Logs.Count(nameof(JobLog.SettleFailed)) >= 1, () => _fixture.Clock.Advance(TimeSpan.FromSeconds(1)));

        _faults.Recover();
        await Eventually.CompletesAsync(running, () => _fixture.Clock.Advance(TimeSpan.FromSeconds(1)));

        _fixture.Probe.Runs.Count.ShouldBe(1);
        _fixture.Job(id).Status.ShouldBe(JobStatus.Succeeded);
        _fixture.ShouldHaveRecorded(id, (1, JobAttemptOutcome.Succeeded));
    }

    [Fact]
    public async Task ABriefOutageDoesNotLoseAFailure()
    {
        _fixture.Probe.Behavior = (_, _) =>
        {
            _faults.FailEverything();
            throw new InvalidOperationException("It broke.");
        };
        var id = await _fixture.EnqueueAsync();

        var running = Task.Run(() => FaultyExecutor().RunNextAsync(Queue, "A", Token), Token);
        await Eventually.HoldsAsync(() => _fixture.Logs.Count(nameof(JobLog.SettleFailed)) >= 1, () => _fixture.Clock.Advance(TimeSpan.FromSeconds(1)));

        _faults.Recover();
        await Eventually.CompletesAsync(running, () => _fixture.Clock.Advance(TimeSpan.FromSeconds(1)));

        var job = _fixture.Job(id);
        job.Status.ShouldBe(JobStatus.Pending);
        job.LastError.ShouldNotBeNull().ShouldContain("It broke.");
        _fixture.ShouldHaveRecorded(id, (1, JobAttemptOutcome.Failed));
    }

    [Fact]
    public async Task AnOutageLongerThanTheLeaseGivesUpAndTheJobRunsAgain()
    {
        _fixture.Probe.Behavior = (_, context) =>
        {
            if (context.Attempt == 1)
            {
                _faults.FailEverything();
            }

            return Task.CompletedTask;
        };
        var id = await _fixture.EnqueueAsync();

        var running = Task.Run(() => FaultyExecutor().RunNextAsync(Queue, "A", Token), Token);
        await Eventually.HoldsAsync(() => running.IsCompleted, () => _fixture.Clock.Advance(TimeSpan.FromSeconds(30)));
        await Should.ThrowAsync<InvalidOperationException>(running);

        _faults.Recover();
        _fixture.Clock.Advance(Lease + TimeSpan.FromTicks(1));
        (await _fixture.Executor.RunNextAsync(Queue, "B", Token)).ShouldBeTrue();

        _fixture.Job(id).Status.ShouldBe(JobStatus.Succeeded);
        _fixture.ShouldHaveRecorded(id, (1, JobAttemptOutcome.LeaseExpired), (2, JobAttemptOutcome.Succeeded));
    }

    // The insert of the attempt, and on SQLite the select that its claim begins with.
    public static TheoryData<string> ClaimCommands => TestProvider.IsSqlite
        ? new() { "INSERT INTO \"cms_job_attempt\"", "SELECT \"c\".\"id\"" }
        : new() { $"INSERT INTO {TestProvider.Quote("cms_job_attempt")}" };

    [Theory]
    [MemberData(nameof(ClaimCommands))]
    public async Task AFailedClaimDoesNotUseUpAnAttempt(string failingCommand)
    {
        var id = await _fixture.EnqueueAsync(options: new JobOptions { MaxAttempts = 1 });
        var worker = FaultyExecutor();

        _faults.Fail(command => command.CommandText.Contains(failingCommand, StringComparison.Ordinal));
        await Should.ThrowAsync<Exception>(() => worker.RunNextAsync(Queue, "A", Token));

        var job = _fixture.Job(id);
        job.Status.ShouldBe(JobStatus.Pending);
        job.Attempts.ShouldBe(0);
        job.LeaseOwner.ShouldBeNull();
        job.LeaseUntil.ShouldBeNull();
        _fixture.Attempts(id).ShouldBeEmpty();
        _fixture.Probe.Runs.ShouldBeEmpty();

        _faults.Recover();
        _fixture.Clock.Advance(Lease + TimeSpan.FromTicks(1));
        (await _fixture.Store.SweepAsync()).ShouldBe(0);
        (await worker.RunNextAsync(Queue, "A", Token)).ShouldBeTrue();

        _fixture.Job(id).Status.ShouldBe(JobStatus.Succeeded);
    }

    [Fact]
    public async Task ACancelledClaimLeavesTheJobAlone()
    {
        var id = await _fixture.EnqueueAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => _fixture.Store.ClaimAsync(Queue, "A", new CancellationToken(canceled: true)));

        _fixture.Job(id).Attempts.ShouldBe(0);
    }

    [SqliteFact("ids that SQLite stores as text")]
    public async Task ARowWithALowercaseIdDoesNotStopTheQueue()
    {
        const string Lowercase = "018f0000-0000-7000-8000-00000000abcd";
        AddRawRow(Lowercase);
        var good = await _fixture.EnqueueAsync("good");

        (await _fixture.Executor.RunNextAsync(Queue, "A", Token)).ShouldBeTrue();
        (await _fixture.Executor.RunNextAsync(Queue, "A", Token)).ShouldBeTrue();

        var id = Guid.Parse(Lowercase);
        _fixture.Job(id).Status.ShouldBe(JobStatus.Succeeded);
        _fixture.Job(good).Status.ShouldBe(JobStatus.Succeeded);
        _fixture.ShouldHaveRecorded(id, (1, JobAttemptOutcome.Succeeded));
        _fixture.Database.ReadStrings("select id from cms_job where id = upper(id)").Count.ShouldBe(2);
    }

    [SqliteFact("ids that SQLite stores as text")]
    public async Task ARowWhoseIdIsNotAGuidDoesNotStopTheQueue()
    {
        AddRawRow("not-a-guid");
        var good = await _fixture.EnqueueAsync("good");

        var claim = _fixture.Store.ClaimAsync(Queue, "A", Token);

        (await claim).ShouldNotBeNull().Id.ShouldBe(good);
    }

    [Fact]
    public async Task ALeaseIsFencedByTheAttemptAsWellAsTheOwner()
    {
        var id = await _fixture.EnqueueAsync();

        var stale = (await _fixture.Store.ClaimAsync(Queue, "A", Token)).ShouldNotBeNull();
        _fixture.Clock.Advance(Lease + TimeSpan.FromTicks(1));
        var current = (await _fixture.Store.ClaimAsync(Queue, "A", Token)).ShouldNotBeNull();
        current.Attempt.ShouldBe(2);

        (await _fixture.Store.RenewLeaseAsync(stale)).ShouldBeNull();
        (await _fixture.Store.SucceedAsync(stale)).ShouldBeFalse();
        (await _fixture.Store.FailAsync(stale, "too late")).ShouldBeFalse();
        (await _fixture.Store.ReleaseAsync(stale)).ShouldBeFalse();

        _fixture.Job(id).Status.ShouldBe(JobStatus.Running);
        (await _fixture.Store.SucceedAsync(current)).ShouldBeTrue();
        _fixture.ShouldHaveRecorded(id, (1, JobAttemptOutcome.LeaseExpired), (2, JobAttemptOutcome.Succeeded));
    }

    [Fact]
    public async Task ARunnerOutOfReachOfTheDatabaseCarriesOnWhenItReturns()
    {
        var runner = _fixture.NewInstance(
            TimeProvider.System,
            options =>
            {
                options.Queues[Queue] = 2;
                options.PollInterval = TimeSpan.FromMilliseconds(5);
                options.MaintenanceInterval = TimeSpan.FromMilliseconds(5);
            },
            _faults).GetRequiredService<IJobRunner>();

        _faults.FailEverything();
        await runner.StartAsync(Token);
        await Eventually.HoldsAsync(() =>
            _fixture.Logs.Count(nameof(JobLog.WorkerFailed)) >= 4 && _fixture.Logs.Count(nameof(JobLog.MaintenanceFailed)) >= 4);

        _faults.Recover();
        var id = await _fixture.EnqueueAsync();
        await Eventually.HoldsAsync(() => _fixture.Job(id).Status == JobStatus.Succeeded);

        await runner.StopAsync(Token);
    }

    [Fact]
    public async Task ARecurringFailureDoesNotKeepTheSweepFromRunning()
    {
        var stuck = await _fixture.EnqueueAsync(options: new JobOptions { MaxAttempts = 1, Queue = "no-workers" });
        await _fixture.Store.ClaimAsync("no-workers", "dead", Token);

        var runner = _fixture.NewInstance(configure: options => options.Queues.Clear(), faults: _faults).GetRequiredService<IJobRunner>();
        _faults.Fail(command => command.CommandText.Contains("cms_recurring_job", StringComparison.Ordinal));
        await runner.StartAsync(Token);

        await Eventually.HoldsAsync(
            () => _fixture.Job(stuck).Status == JobStatus.DeadLettered,
            () => _fixture.Clock.Advance(Defaults.MaintenanceInterval));

        _fixture.Logs.Count(nameof(JobLog.MaintenanceFailed)).ShouldBeGreaterThan(0);
        await Eventually.CompletesAsync(runner.StopAsync(Token));
    }

    [Fact]
    public async Task OneScheduleThatCannotFireDoesNotStopTheOthers()
    {
        var scheduler = _fixture.Scheduler;
        await scheduler.AddRecurringAsync<ProbeJob, ProbePayload>("broken", "* * * * *", new ProbePayload("broken"), queue: null, cancellationToken: Token);
        await scheduler.AddRecurringAsync<ProbeJob, ProbePayload>("fine", "* * * * *", new ProbePayload("fine"), queue: null, cancellationToken: Token);
        _fixture.Clock.Advance(TimeSpan.FromMinutes(1));

        var recurring = _fixture.NewInstance(faults: _faults).GetRequiredService<RecurringJobs>();
        var brokenDue = _fixture.Recurring("broken").NextRunAt;
        _faults.Fail(command => command.CommandText.StartsWith("UPDATE", StringComparison.Ordinal)
            && command.Parameters.Cast<DbParameter>().Any(parameter => Equals(parameter.Value, "broken")));

        await recurring.FireDueAsync(Token);

        _fixture.Jobs().ShouldHaveSingleItem().Payload.ShouldContain("fine");
        _fixture.Recurring("broken").NextRunAt.ShouldBe(brokenDue);
        _fixture.Logs.Count(nameof(JobLog.RecurringFireFailed)).ShouldBe(1);

        _faults.Recover();
        await recurring.FireDueAsync(Token);

        _fixture.Jobs().Count.ShouldBe(2);
    }

    // A lease lapses in the middle of a job while two real runners are up: the instance that lost the database stops
    // its attempt, and the other takes the job over with no moment where both run it.
    [Fact]
    public async Task WhenALeaseLapsesMidJobTheTwoRunnersNeverOverlap()
    {
        var active = 0;
        var most = 0;
        _fixture.Probe.Behavior = async (_, context) =>
        {
            var now = Interlocked.Increment(ref active);
            InterlockedMax(ref most, now);
            try
            {
                if (context.Attempt == 1)
                {
                    await Task.Delay(Timeout.Infinite, context.CancellationToken);
                }
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        };
        var id = await _fixture.EnqueueAsync();

        var first = _fixture.NewInstance(configure: options => options.Queues[Queue] = 1, faults: _faults).GetRequiredService<IJobRunner>();
        await first.StartAsync(Token);
        await Eventually.HoldsAsync(() => !_fixture.Probe.Runs.IsEmpty);
        _faults.FailEverything();

        var second = _fixture.NewInstance(configure: options => options.Queues[Queue] = 1).GetRequiredService<IJobRunner>();
        await second.StartAsync(Token);

        // The first runner stops a heartbeat before its lease ends, which is a minute of real time and not the few
        // milliseconds between two nudges, so the clock waits there until it has stopped.
        var stopBy = JobFixture.Start + Lease - Beat;
        await Eventually.HoldsAsync(() => _fixture.Job(id).Status == JobStatus.Succeeded, () =>
        {
            if (_fixture.Clock.GetUtcNow() < stopBy || Volatile.Read(ref active) == 0)
            {
                _fixture.Clock.Advance(Beat);
            }
        });

        _fixture.Job(id).Attempts.ShouldBe(2);
        _fixture.Probe.Runs.Count.ShouldBe(2);
        most.ShouldBe(1);
        _fixture.ShouldHaveRecorded(id, (1, JobAttemptOutcome.LeaseExpired), (2, JobAttemptOutcome.Succeeded));

        _faults.Recover();
        await Eventually.CompletesAsync(first.StopAsync(Token), () => _fixture.Clock.Advance(Beat));
        await Eventually.CompletesAsync(second.StopAsync(Token), () => _fixture.Clock.Advance(Beat));
    }

    [Fact]
    public async Task AJobThatIgnoresTheLapseCannotOverwriteTheWorkerThatTookOver()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fixture.Probe.Behavior = (_, context) => context.Attempt == 1 ? gate.Task : Task.CompletedTask;
        var id = await _fixture.EnqueueAsync();

        var stalled = Task.Run(() => FaultyExecutor().RunNextAsync(Queue, "A", Token), Token);
        await Eventually.HoldsAsync(() => !_fixture.Probe.Runs.IsEmpty);
        _faults.FailEverything();
        await BeatAsync(4);

        _fixture.Clock.Advance(Beat + TimeSpan.FromTicks(1));
        (await _fixture.Executor.RunNextAsync(Queue, "B", Token)).ShouldBeTrue();
        _fixture.Job(id).Status.ShouldBe(JobStatus.Succeeded);
        var completedAt = _fixture.Job(id).CompletedAt;

        _faults.Recover();
        gate.SetResult();
        (await stalled).ShouldBeTrue();

        _fixture.Job(id).CompletedAt.ShouldBe(completedAt);
        _fixture.ShouldHaveRecorded(id, (1, JobAttemptOutcome.LeaseExpired), (2, JobAttemptOutcome.Succeeded));
    }

    private void AddRawRow(string id) => _fixture.Database.Execute(
        $"INSERT INTO cms_job(id, type, payload, queue, run_at, priority, status, attempts, max_attempts, created_at, origin) VALUES ('{id}', '{JobFixture.ProbeType}', '{{}}', '{Queue}', {JobFixture.Start.AddHours(-1).UtcTicks}, 0, 'pending', 0, 3, 0, 'Job')");

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while (value > (seen = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, seen) != seen)
        {
        }
    }
}
