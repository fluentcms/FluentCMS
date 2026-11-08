using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;
using FluentCMS.Content;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Jobs;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Jobs;

public sealed class JobRetentionTests : IDisposable
{
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);

    private readonly JobFixture _fixture = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static DateTimeOffset Now => JobFixture.Start;

    public void Dispose() => _fixture.Dispose();

    private Guid Add(string status, DateTimeOffset? completed, int attempts = 2)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        var job = new Job
        {
            Type = JobFixture.ProbeType,
            Payload = "{}",
            Queue = JobRunnerOptions.DefaultQueue,
            RunAt = Now,
            MaxAttempts = 3,
            Attempts = attempts,
            Status = status,
            CompletedAt = completed,
            CreatedAt = Now,
            Origin = Origin.Job,
        };
        context.Add(job);
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            context.Add(new JobAttempt { JobId = job.Id, Attempt = attempt, StartedAt = Now, FinishedAt = completed, Outcome = JobAttemptOutcome.Succeeded });
        }

        context.SaveChanges();
        return job.Id;
    }

    private Guid Succeeded(TimeSpan ago, int attempts = 2) => Add(JobStatus.Succeeded, Now - ago, attempts);

    private HashSet<Guid> Remaining()
    {
        using var scope = _fixture.Services.CreateScope();
        return [.. scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<Job>().AsNoTracking().Select(job => job.Id)];
    }

    private List<Guid> AttemptOwners()
    {
        using var scope = _fixture.Services.CreateScope();
        return [.. scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<JobAttempt>().AsNoTracking().Select(attempt => attempt.JobId)];
    }

    private static JobContext ContextFor(CancellationToken token) =>
        new(Guid.CreateVersion7(), JobRetentionJob.Type, JobRunnerOptions.DefaultQueue, 1, 3, new OperationContext(Principal.System, Origin.Job, new SiteId(Guid.Empty), "en", null, "retention", Now), token);

    private static async Task RunAsync(IServiceProvider instance, CancellationToken token)
    {
        using var scope = instance.CreateScope();
        await scope.ServiceProvider.GetRequiredService<JobRetentionJob>().ExecuteAsync(new JobRetentionPayload(), ContextFor(token));
    }

    private Task RunAsync() => RunAsync(_fixture.Services, Token);

    [Fact]
    public async Task SucceededJobsPastTheRetentionAreRemovedAndNewerOnesStay()
    {
        Succeeded(Week + TimeSpan.FromMinutes(1));
        Succeeded(TimeSpan.FromDays(40));
        var edge = Succeeded(Week);
        var recent = Succeeded(TimeSpan.FromHours(2));

        await RunAsync();

        Remaining().ShouldBe([edge, recent], ignoreOrder: true);
    }

    [Fact]
    public async Task TheRetentionComesFromTheOptions()
    {
        using var shorter = _fixture.NewInstance(configure: options => options.SucceededRetention = TimeSpan.FromHours(1));
        Succeeded(TimeSpan.FromHours(2));
        var recent = Succeeded(TimeSpan.FromMinutes(10));

        await RunAsync(shorter, Token);

        Remaining().ShouldBe([recent]);
    }

    [Fact]
    public async Task PendingRunningAndDeadLetteredJobsAreNeverRemovedHoweverOldTheyAre()
    {
        var ancient = TimeSpan.FromDays(400);
        var pending = Add(JobStatus.Pending, Now - ancient);
        var running = Add(JobStatus.Running, Now - ancient);
        var dead = Add(JobStatus.DeadLettered, Now - ancient);
        var pendingWithoutDate = Add(JobStatus.Pending, null);
        var gone = Succeeded(ancient);

        await RunAsync();

        Remaining().ShouldBe([pending, running, dead, pendingWithoutDate], ignoreOrder: true);
        Remaining().ShouldNotContain(gone);
        AttemptOwners().Distinct().ShouldBe([pending, running, dead, pendingWithoutDate], ignoreOrder: true);
    }

    [Fact]
    public async Task RecurringDefinitionsAreLeftAlone()
    {
        await _fixture.Scheduler.AddRecurringAsync<ProbeJob, ProbePayload>("keep", "0 * * * *", new ProbePayload("r"), queue: null, cancellationToken: Token);
        using (var scope = _fixture.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<RecurringJob>().ExecuteUpdateAsync(set => set.SetProperty(job => job.NextRunAt, Now - TimeSpan.FromDays(100)), Token);
        }

        Succeeded(TimeSpan.FromDays(100));

        await RunAsync();

        _fixture.Recurring("keep").Cron.ShouldBe("0 * * * *");
    }

    [Fact]
    public async Task TheAttemptsGoWithTheirJobAndNoOtherAttemptIsTouched()
    {
        var gone = Succeeded(TimeSpan.FromDays(30), attempts: 3);
        var kept = Succeeded(TimeSpan.FromHours(1), attempts: 2);
        var dead = Add(JobStatus.DeadLettered, Now - TimeSpan.FromDays(30), attempts: 3);

        await RunAsync();

        _fixture.Attempts(gone).ShouldBeEmpty();
        _fixture.Attempts(kept).Count.ShouldBe(2);
        _fixture.Attempts(dead).Count.ShouldBe(3);
        AttemptOwners().Except(Remaining()).ShouldBeEmpty();
    }

    [Fact]
    public async Task ABacklogLargerThanABatchIsRemovedInSeveralAndTheRestStays()
    {
        using var small = _fixture.NewInstance(configure: options => options.RetentionBatchSize = 3);
        var old = Enumerable.Range(0, 11).Select(index => Succeeded(TimeSpan.FromDays(10 + index), attempts: 1)).ToList();
        var recent = Succeeded(TimeSpan.FromMinutes(5));
        var dead = Add(JobStatus.DeadLettered, Now - TimeSpan.FromDays(50));

        await RunAsync(small, Token);

        Remaining().ShouldBe([recent, dead], ignoreOrder: true);
        AttemptOwners().Distinct().ShouldBe([recent, dead], ignoreOrder: true);
        old.ShouldAllBe(id => !Remaining().Contains(id));
    }

    [Fact]
    public async Task ABatchDeletesAtMostTheBatchSize()
    {
        var deletes = new List<int>();
        using var small = _fixture.NewInstance(configure: options => options.RetentionBatchSize = 4, faults: null, cms: cms =>
            cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(new DeleteCounter(deletes))));
        foreach (var index in Enumerable.Range(0, 10))
        {
            Succeeded(TimeSpan.FromDays(10 + index), attempts: 1);
        }

        await RunAsync(small, Token);

        Remaining().ShouldBeEmpty();
        deletes.ShouldNotBeEmpty();
        deletes.ShouldAllBe(count => count <= 4);
        deletes.Sum().ShouldBe(10);
    }

    [Fact]
    public async Task RunningAgainFindsNothingLeftAndChangesNothing()
    {
        var kept = Succeeded(TimeSpan.FromHours(1));
        Succeeded(TimeSpan.FromDays(30));

        await RunAsync();
        await RunAsync();

        Remaining().ShouldBe([kept]);
        _fixture.Attempts(kept).Count.ShouldBe(2);
    }

    [Fact]
    public async Task AFailedRunLeavesJobsWithTheirAttemptsAndTheNextRunFinishesTheWork()
    {
        var faults = new JobFaults();
        var seen = 0;
        faults.Fail(command =>
        {
            var batch = command.CommandText.StartsWith("DELETE FROM \"cms_job\"", StringComparison.Ordinal) && Interlocked.Increment(ref seen) == 2;
            return batch;
        });
        using var small = _fixture.NewInstance(configure: options => options.RetentionBatchSize = 2, faults: faults);
        var old = Enumerable.Range(0, 6).Select(index => Succeeded(TimeSpan.FromDays(10 + index))).ToList();

        await Should.ThrowAsync<InvalidOperationException>(() => RunAsync(small, Token));

        var left = Remaining();
        left.Count.ShouldBe(4);
        AttemptOwners().Except(left).ShouldBeEmpty();
        left.ShouldAllBe(id => AttemptOwners().Count(owner => owner == id) == 2);

        faults.Recover();
        await RunAsync(small, Token);

        Remaining().ShouldBeEmpty();
        AttemptOwners().ShouldBeEmpty();
        old.Count.ShouldBe(6);
    }

    [Fact]
    public async Task ACancelledRunStopsAndKeepsWhatIsLeftForTheNextRun()
    {
        Succeeded(TimeSpan.FromDays(30));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => RunAsync(_fixture.Services, cancelled.Token));
        Remaining().Count.ShouldBe(1);

        await RunAsync();
        Remaining().ShouldBeEmpty();
    }

    [Fact]
    public async Task TwoInstancesRunningAtOnceRemoveEachJobOnceAndFailNeither()
    {
        using var first = _fixture.NewInstance(configure: options => options.RetentionBatchSize = 5);
        using var second = _fixture.NewInstance(configure: options => options.RetentionBatchSize = 5);
        var kept = Succeeded(TimeSpan.FromHours(1));
        foreach (var index in Enumerable.Range(0, 60))
        {
            Succeeded(TimeSpan.FromDays(10 + index), attempts: 2);
        }

        await Task.WhenAll(Task.Run(() => RunAsync(first, Token), Token), Task.Run(() => RunAsync(second, Token), Token));

        Remaining().ShouldBe([kept]);
        AttemptOwners().ShouldBe([kept, kept], ignoreOrder: true);
    }

    [SqliteFact("SQLite's query plans")]
    public async Task EveryBatchIsSelectedFromAnIndexWithNoSort()
    {
        var plans = new List<string>();
        using var instance = _fixture.NewInstance(configure: options => options.RetentionBatchSize = 2, cms: cms =>
            cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(new PlanCollector(plans))));
        foreach (var index in Enumerable.Range(0, 5))
        {
            Succeeded(TimeSpan.FromDays(10 + index));
        }

        await RunAsync(instance, Token);

        Remaining().ShouldBeEmpty();
        plans.Count.ShouldBeGreaterThanOrEqualTo(2);
        plans.ShouldAllBe(plan => !plan.Contains("TEMP B-TREE", StringComparison.Ordinal));
        plans.ShouldAllBe(plan => plan.Contains("ix_cms_job_status_lease", StringComparison.Ordinal));
        plans.ShouldAllBe(plan => !plan.Contains("SCAN cms_job\n", StringComparison.Ordinal) || plan.Contains("USING", StringComparison.Ordinal));
    }

    [Fact]
    public void TheJobTakesAnAttemptTimeoutSoThatAPruneCannotHoldAWorkerForGood()
    {
        var registration = _fixture.Services.GetRequiredService<JobRegistry>().Find(JobRetentionJob.Type);

        registration.ShouldNotBeNull();
        registration.AttemptTimeout.ShouldBe(JobRetentionJob.AttemptTimeout);
        registration.AttemptTimeout!.Value.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task TheScheduleIsStoredAtStartupOnceHoweverOftenTheHostStarts()
    {
        using var instance = _fixture.NewInstance(configure: options => options.RetentionSchedule = "0 5 * * *");
        var service = instance.GetServices<IHostedService>().OfType<JobRetentionScheduleService>().Single();

        await service.StartAsync(Token);
        await service.StopAsync(Token);
        await service.StartAsync(Token);
        await service.StopAsync(Token);

        using var scope = _fixture.Services.CreateScope();
        var stored = scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<RecurringJob>().AsNoTracking().Where(job => job.Name == JobRetentionJob.RecurringName).ToList();
        var job = stored.ShouldHaveSingleItem();
        job.Type.ShouldBe(JobRetentionJob.Type);
        job.Cron.ShouldBe("0 5 * * *");
    }

    [Fact]
    public async Task ADatabaseThatIsDownAtStartupIsRetriedInTheBackground()
    {
        var faults = new JobFaults();
        faults.FailEverything();
        using var instance = _fixture.NewInstance(faults: faults);
        var service = instance.GetServices<IHostedService>().OfType<JobRetentionScheduleService>().Single();

        await service.StartAsync(Token);
        using (var scope = _fixture.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<RecurringJob>().Any(job => job.Name == JobRetentionJob.RecurringName).ShouldBeFalse();
        }

        faults.Recover();
        await Eventually.HoldsAsync(
            () =>
            {
                using var scope = _fixture.Services.CreateScope();
                return scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<RecurringJob>().Any(job => job.Name == JobRetentionJob.RecurringName);
            },
            () => _fixture.Clock.Advance(TimeSpan.FromSeconds(30)));

        await service.StopAsync(Token);
    }

    [Fact]
    public void TheDefaultsAreValid()
    {
        Should.NotThrow(() => new JobRunnerOptions().Validate());
        new JobRunnerOptions().SucceededRetention.ShouldBe(TimeSpan.FromDays(7));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ARetentionThatIsNotPositiveIsRefused(int days) =>
        Refused(options => options.SucceededRetention = TimeSpan.FromDays(days));

    [Fact]
    public void ARetentionBeyondAHundredYearsIsRefused() =>
        Refused(options => options.SucceededRetention = TimeSpan.MaxValue);

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(JobRunnerOptions.MaxRetentionBatchSize + 1)]
    public void ABatchSizeOutsideTheRangeIsRefused(int size) =>
        Refused(options => options.RetentionBatchSize = size);

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("every day")]
    [InlineData("0 0 31 2 *")]
    public void AScheduleThatIsNotACronThatOccursIsRefused(string cron) =>
        Refused(options => options.RetentionSchedule = cron);

    [Fact]
    public void TheLimitsOfTheRangeAreAccepted()
    {
        var options = new JobRunnerOptions { SucceededRetention = TimeSpan.FromTicks(1), RetentionBatchSize = JobRunnerOptions.MaxRetentionBatchSize };
        Should.NotThrow(options.Validate);

        options.SucceededRetention = JobRunnerOptions.LongestRetention;
        options.RetentionBatchSize = 1;
        Should.NotThrow(options.Validate);
    }

    private static void Refused(Action<JobRunnerOptions> configure)
    {
        var options = new JobRunnerOptions();
        configure(options);

        Should.Throw<FluentCmsConfigurationException>(options.Validate).Code.ShouldBe(JobErrorCodes.InvalidRunnerOptions);
    }

    // Counts the rows each delete of cms_job removed.
    private sealed class DeleteCounter(List<int> deletes) : DbCommandInterceptor
    {
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("DELETE FROM \"cms_job\"", StringComparison.Ordinal))
            {
                lock (deletes)
                {
                    deletes.Add(result);
                }
            }

            return ValueTask.FromResult(result);
        }
    }

    // Asks SQLite how it runs each batch the job selects, with the parameters the job gave it.
    private sealed class PlanCollector(List<string> plans) : DbCommandInterceptor
    {
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Collect(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Collect(command);
            return ValueTask.FromResult(result);
        }

        private void Collect(DbCommand command)
        {
            if (Regex.IsMatch(command.CommandText, @"^SELECT ""c"".""id""\s+FROM ""cms_job"""))
            {
                using var explain = command.Connection!.CreateCommand();
                explain.Transaction = command.Transaction;
#pragma warning disable CA2100 // The text of a command EF built.
                explain.CommandText = "EXPLAIN QUERY PLAN " + command.CommandText;
#pragma warning restore CA2100
                foreach (DbParameter parameter in command.Parameters)
                {
                    var copy = explain.CreateParameter();
                    copy.ParameterName = parameter.ParameterName;
                    copy.Value = parameter.Value;
                    explain.Parameters.Add(copy);
                }

                using var reader = explain.ExecuteReader();
                var plan = new StringBuilder();
                while (reader.Read())
                {
                    plan.AppendLine(reader.GetString(3));
                }

                lock (plans)
                {
                    plans.Add(plan.ToString());
                }
            }
        }
    }
}
