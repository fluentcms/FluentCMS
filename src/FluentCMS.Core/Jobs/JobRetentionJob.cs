using FluentCMS.Core.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Jobs;

internal sealed record JobRetentionPayload;

// Removes the jobs that succeeded longer ago than SucceededRetention and, when DeadLetteredRetention is set, the jobs that
// were dead-lettered longer ago than it, with their attempts. Pending and running jobs and the recurring definitions stay.
// The jobs go in batches, each taken from the index on status and completed_at without an order, since an order would sort
// every finished job. A removed job does not come back, so the next batch is the next jobs due. Each batch removes the
// attempts and the jobs in one transaction, and checks the conditions again in the delete, so that two instances running
// at once remove a job once and a run after a failed one takes up where it stopped.
internal sealed class JobRetentionJob(SystemDbContext context, ScheduleStore schedules, JobRunnerOptions options, TimeProvider time, ILogger<JobRetentionJob> logger) : IJob<JobRetentionPayload>, IResumableJob
{
    public const string Type = "jobs.retention";

    public const string ScheduleJobRemoved = "The job was dead-lettered and then removed by the job retention, so the schedule never ran.";

    public const string RecurringName = "jobs.retention";

    // Stops a large prune from holding a worker for good; what is left waits for the next run.
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromMinutes(10);

    public async Task ExecuteAsync(JobRetentionPayload payload, JobContext jobContext)
    {
        var cancellationToken = jobContext.CancellationToken;
        var now = time.GetUtcNow();
        var succeeded = await RemoveAsync(JobStatus.Succeeded, now - options.SucceededRetention, cancellationToken);
        var deadLettered = 0;
        if (options.DeadLetteredRetention is { } retention)
        {
            deadLettered = await RemoveAsync(JobStatus.DeadLettered, now - retention, cancellationToken);
        }

        JobLog.Retention(logger, succeeded, deadLettered);
    }

    private async Task<int> RemoveAsync(string status, DateTimeOffset before, CancellationToken cancellationToken)
    {
        var removed = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ids = await Expired(status, before).Select(job => job.Id).Take(options.RetentionBatchSize).ToListAsync(cancellationToken);
            if (ids.Count == 0)
            {
                return removed;
            }

            if (status == JobStatus.DeadLettered)
            {
                await FailSchedulesAsync(ids, before, cancellationToken);
            }

            removed += await DeleteAsync(ids, status, before, cancellationToken);

            if (ids.Count < options.RetentionBatchSize)
            {
                return removed;
            }
        }
    }

    // A crash between the dead-lettering of a scheduled publish or unpublish and ScheduleDeadLetterHandler leaves its schedule
    // pending. Once its job is gone nothing would run or close it, so it is failed before the job is removed. ScheduleStore
    // writes through its own context, so this runs before the batch's transaction, which would hold SQLite's write lock.
    private async Task FailSchedulesAsync(List<Guid> ids, DateTimeOffset before, CancellationToken cancellationToken)
    {
        var orphaned = await context.Set<ScheduleRow>().AsNoTracking()
            .Where(row => row.Status == ScheduleRow.Pending && row.JobId != null && ids.Contains(row.JobId.Value)
                && context.Set<Job>().Any(job => job.Id == row.JobId && job.Status == JobStatus.DeadLettered && job.CompletedAt < before))
            .Select(row => row.JobId!.Value)
            .ToListAsync(cancellationToken);

        foreach (var job in orphaned)
        {
            await schedules.FailByJobAsync(job, time.GetUtcNow(), ScheduleJobRemoved, cancellationToken);
        }
    }

    private IQueryable<Job> Expired(string status, DateTimeOffset before) =>
        context.Set<Job>().AsNoTracking().Where(job => job.Status == status && job.CompletedAt < before);

    private async Task<int> DeleteAsync(List<Guid> ids, string status, DateTimeOffset before, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        await context.Set<JobAttempt>()
            .Where(attempt => ids.Contains(attempt.JobId)
                && context.Set<Job>().Any(job => job.Id == attempt.JobId && job.Status == status && job.CompletedAt < before))
            .ExecuteDeleteAsync(cancellationToken);

        var removed = await context.Set<Job>()
            .Where(job => ids.Contains(job.Id) && job.Status == status && job.CompletedAt < before)
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return removed;
    }
}

// Stores the schedule while the host starts, so that it is there when startup is over. If the database cannot be reached
// then, it keeps trying in the background, since a host without the schedule would otherwise keep its finished jobs for good.
internal sealed class JobRetentionScheduleService(IJobScheduler scheduler, JobRunnerOptions options, TimeProvider time, ILogger<JobRetentionScheduleService> logger) : BackgroundService
{
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(30);

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!await TryScheduleAsync(cancellationToken))
        {
            await base.StartAsync(cancellationToken);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Retry, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (await TryScheduleAsync(stoppingToken))
            {
                return;
            }
        }
    }

    private async Task<bool> TryScheduleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await scheduler.AddRecurringAsync<JobRetentionJob, JobRetentionPayload>(JobRetentionJob.RecurringName, options.RetentionSchedule, new JobRetentionPayload(), queue: null, cancellationToken: cancellationToken);
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            JobLog.RetentionScheduleFailed(logger, exception);
            return false;
        }
    }
}
