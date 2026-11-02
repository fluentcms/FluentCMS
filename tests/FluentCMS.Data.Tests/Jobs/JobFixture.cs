using System.Collections.Concurrent;
using FluentCMS.Content;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Sqlite;
using FluentCMS.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Jobs;

public sealed record ProbePayload(string Key = "", string? Secret = null, JobActor? Actor = null) : IJobActorPayload;

internal sealed record JobRun(ProbePayload Payload, JobContext Context, OperationContext? Ambient);

// What the probe job did, shared by every instance of a test, and what it should do next.
internal sealed class JobProbe
{
    public ConcurrentQueue<JobRun> Runs { get; } = new();

    public Func<ProbePayload, JobContext, Task> Behavior { get; set; } = (_, _) => Task.CompletedTask;

    public int RunsOf(string key) => Runs.Count(run => run.Payload.Key == key);
}

internal sealed class ProbeJob(JobProbe probe, IOperationContextAccessor operation) : IJob<ProbePayload>
{
    public Task ExecuteAsync(ProbePayload payload, JobContext context)
    {
        probe.Runs.Enqueue(new JobRun(payload, context, operation.Current));
        return probe.Behavior(payload, context);
    }
}

internal sealed class RecordingJobEvents : IJobEventSink
{
    public ConcurrentQueue<JobFailedEvent> Failed { get; } = new();

    public ConcurrentQueue<JobDeadLetteredEvent> DeadLettered { get; } = new();

    public bool Throws { get; set; }

    public Task PublishAsync(JobFailedEvent failed)
    {
        Failed.Enqueue(failed);
        return Throws ? throw new InvalidOperationException("The sink is down.") : Task.CompletedTask;
    }

    public Task PublishAsync(JobDeadLetteredEvent deadLettered)
    {
        DeadLettered.Enqueue(deadLettered);
        return Throws ? throw new InvalidOperationException("The sink is down.") : Task.CompletedTask;
    }
}

// A migrated file database, with any number of instances on it. Each instance is its own container, as a second
// process would be, and shares only the database, the clock and the probe.
internal sealed class JobFixture : IDisposable
{
    public const string ProbeType = "probe";

    public static readonly DateTimeOffset Start = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly List<ServiceProvider> _instances = [];

    public JobFixture()
    {
        Services = NewInstance();
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<SystemDbContext>().Database.Migrate();
    }

    public SqliteFileDatabase Database { get; } = new();

    public ManualTimeProvider Clock { get; } = new(Start);

    public JobProbe Probe { get; } = new();

    public RecordingJobEvents Events { get; } = new();

    // The first instance, for tests with only one.
    public ServiceProvider Services { get; }

    public IJobScheduler Scheduler => Services.GetRequiredService<IJobScheduler>();

    public JobExecutor Executor => Services.GetRequiredService<JobExecutor>();

    public JobStore Store => Services.GetRequiredService<JobStore>();

    public ServiceProvider NewInstance(TimeProvider? time = null, Action<JobRunnerOptions>? configure = null)
    {
        var instance = Database.CreateServices(configure: cms =>
        {
            cms.Services.AddSingleton(time ?? Clock);
            cms.Services.AddSingleton(Probe);
            cms.Services.AddSingleton<IJobEventSink>(Events);
            cms.Jobs.AddJob<ProbeJob, ProbePayload>(ProbeType);
            configure?.Invoke(cms.Jobs.Options);
        });

        _instances.Add(instance);
        return instance;
    }

    public Task<Guid> EnqueueAsync(string key = "a", JobOptions? options = null, string? secret = null, JobActor? actor = null) =>
        Scheduler.EnqueueAsync<ProbeJob, ProbePayload>(new ProbePayload(key, secret, actor), options, TestContext.Current.CancellationToken);

    public Job Job(Guid id)
    {
        using var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<Job>().AsNoTracking().Single(job => job.Id == id);
    }

    public List<JobAttempt> Attempts(Guid id)
    {
        using var scope = Services.CreateScope();
        return [.. scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<JobAttempt>().AsNoTracking().Where(attempt => attempt.JobId == id).OrderBy(attempt => attempt.Attempt)];
    }

    public List<Job> Jobs()
    {
        using var scope = Services.CreateScope();
        return [.. scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<Job>().AsNoTracking()];
    }

    public RecurringJob Recurring(string name)
    {
        using var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<RecurringJob>().AsNoTracking().Single(job => job.Name == name);
    }

    // A row that no scheduler would write, through EF as cms_job needs.
    public Guid AddRow(string type, string payload, int maxAttempts = 3)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        var job = new Job { Type = type, Payload = payload, Queue = JobRunnerOptions.DefaultQueue, RunAt = Start, MaxAttempts = maxAttempts, CreatedAt = Start, Origin = Origin.Job };
        context.Add(job);
        context.SaveChanges();

        return job.Id;
    }

    public void Dispose()
    {
        foreach (var instance in _instances)
        {
            instance.Dispose();
        }

        Database.Dispose();
    }
}

internal static class Eventually
{
    // Waits for work on other threads. The bound is only there so that a hang fails the test: nothing here depends on
    // how long the work takes.
    public static async Task HoldsAsync(Func<bool> condition, Action? nudge = null)
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        bound.CancelAfter(TimeSpan.FromSeconds(60));

        while (!condition())
        {
            nudge?.Invoke();
            await Task.Delay(5, bound.Token);
        }
    }

    public static async Task CompletesAsync(Task task, Action? nudge = null)
    {
        await HoldsAsync(() => task.IsCompleted, nudge);
        await task;
    }
}

internal static class JobAssertions
{
    public static void ShouldHaveRecorded(this JobFixture fixture, Guid job, params (int Attempt, string? Outcome)[] attempts) =>
        fixture.Attempts(job).Select(attempt => (attempt.Attempt, attempt.Outcome)).ShouldBe(attempts);
}
