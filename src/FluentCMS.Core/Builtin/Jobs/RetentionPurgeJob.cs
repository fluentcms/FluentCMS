using System.Text.Json;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Models;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Builtin.Jobs;

// Clears stored prompts and responses past their retention as the retention engine (AUD-04, ADR-0012), a batch at a time until a
// batch clears nothing. Its next run is scheduled first, so a run that fails or dies still leaves one coming.
internal sealed class RetentionPurgeJob(TimeProvider clock) : IJobHandler
{
    public const string JobType = "retention.purge";

    // Well inside any retention of days, so nothing stays long past its time.
    public static readonly TimeSpan Every = TimeSpan.FromDays(1);

    public static JobSubject Subject { get; } = new("retention", SystemInitiator.Retention.Id);

    public string Type => JobType;

    public SystemInitiator RunAs => SystemInitiator.Retention;

    public async Task<Result> RunAsync(JobContext context, CancellationToken cancellationToken)
    {
        await context.Services.GetRequiredService<RetentionSchedule>().ScheduleAsync(context.Job.Id, clock.GetUtcNow() + Every, cancellationToken);

        var log = context.Services.GetRequiredService<IModelCallLog>();
        var total = 0;
        while (true)
        {
            var purged = await log.PurgeAsync(cancellationToken);
            if (!purged.IsSuccess)
            {
                return Result.Failure(purged.Errors);
            }

            total += purged.Value;
            if (!await context.ReportProgressAsync(total, null, $"Purged {total} stored prompts past their retention.", cancellationToken))
            {
                return Result.Failure("retention_lease_lost", "The retention purge stopped: another worker has its job.");
            }

            if (purged.Value == 0)
            {
                return Result.Success();
            }
        }
    }
}

// Keeps one retention run scheduled: a host that starts enqueues one, and each run enqueues the next, unless another run is queued
// or running already, so hosts that start together end with one.
internal sealed class RetentionSchedule(IServiceScopeFactory scopes, TimeProvider clock) : IRetentionSchedule
{
    public Task EnsureScheduledAsync(CancellationToken cancellationToken = default)
    {
        return ScheduleAsync(null, clock.GetUtcNow(), cancellationToken);
    }

    // As the retention engine, so the job records the engine as the one that enqueued it.
    public async Task ScheduleAsync(Guid? current, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using var engine = scopes.CreateEngineScope(SystemInitiator.Retention);
        var services = engine.ServiceProvider;
        var jobs = services.GetRequiredService<IJobQueue>();

        var scheduled = (await jobs.ListForSubjectAsync(RetentionPurgeJob.Subject, cancellationToken))
            .Any(job => job.Type == RetentionPurgeJob.JobType && job.Id != current && job.State is JobState.Queued or JobState.Running);
        if (scheduled)
        {
            return;
        }

        await using var work = await services.GetRequiredService<IUnitOfWork>().BeginAsync(cancellationToken);
        await jobs.EnqueueAsync(
            new NewJob(RetentionPurgeJob.JobType, JsonSerializer.SerializeToElement(new { }))
            {
                Subject = RetentionPurgeJob.Subject,
                RunAt = at,
            },
            cancellationToken);
        await work.CommitAsync(cancellationToken);
    }
}
