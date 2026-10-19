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
internal sealed class RetentionPurgeJob : IJobHandler
{
    public const string JobType = "retention.purge";

    // Well inside any retention of days, so nothing stays long past its time.
    public static readonly TimeSpan Every = TimeSpan.FromDays(1);

    public static JobSubject Subject { get; } = new("retention", SystemInitiator.Retention.Id);

    public string Type => JobType;

    public SystemInitiator RunAs => SystemInitiator.Retention;

    public async Task<Result> RunAsync(JobContext context, CancellationToken cancellationToken)
    {
        // The worker runs this job as the retention engine (EngineJobs); a run as anyone else purges nothing and schedules nothing.
        if (context.Services.GetRequiredService<ICurrentInitiator>().Current != SystemInitiator.Retention)
        {
            return Result.Failure(ModelErrorCodes.PeopleOnly, "The retention purge runs only as the retention engine, and this run is not, so it purges nothing.");
        }

        var clock = context.Services.GetRequiredService<TimeProvider>();
        await context.Services.GetRequiredService<RetentionSchedule>().ScheduleAsync(context.Job.Id, clock.GetUtcNow() + Every, cancellationToken);

        // Each batch in a scope of its own, so what one batch tracks is gone before the next, however long the backlog.
        var scopes = context.Services.GetRequiredService<IServiceScopeFactory>();
        var total = 0;
        while (true)
        {
            Result<int> purged;
            await using (var batch = scopes.CreateEngineScope(SystemInitiator.Retention))
            {
                purged = await batch.ServiceProvider.GetRequiredService<IModelCallLog>().PurgeAsync(cancellationToken);
            }

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

// Keeps a retention run scheduled: a host that starts enqueues one, and each run enqueues the next, unless another run is queued.
// A run enqueues its successor before it purges, so a running run always leaves one queued; two that start together may both
// enqueue one, which the next day's runs fold back into one chain, and none can leave the schedule empty.
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
            .Any(job => job.Type == RetentionPurgeJob.JobType && job.Id != current && job.State == JobState.Queued);
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
