using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Jobs;

internal sealed record JobRetentionPayload;

// Removes the jobs that succeeded longer ago than SucceededRetention, with their attempts. Only succeeded jobs are
// touched, so pending, running and dead-lettered jobs and the recurring definitions stay. The jobs go in batches, each
// taken from the status index without an order, since an order would sort every succeeded job. A removed job does not
// come back, so the next batch is the next jobs due. Each batch removes the attempts and the jobs in one transaction, and
// checks the conditions again in the delete, so that two instances running at once remove a job once and a run after a
// failed one takes up where it stopped.
internal sealed class JobRetentionJob(SystemDbContext context, JobRunnerOptions options, TimeProvider time, ILogger<JobRetentionJob> logger) : IJob<JobRetentionPayload>
{
    public const string Type = "jobs.retention";

    public const string RecurringName = "jobs.retention";

    // Stops a large prune from holding a worker for good; what is left waits for the next run.
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromMinutes(10);

    public async Task ExecuteAsync(JobRetentionPayload payload, JobContext jobContext)
    {
        var cancellationToken = jobContext.CancellationToken;
        var before = time.GetUtcNow() - options.SucceededRetention;
        var removed = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ids = await Expired(before).Select(job => job.Id).Take(options.RetentionBatchSize).ToListAsync(cancellationToken);
            if (ids.Count == 0)
            {
                break;
            }

            removed += await DeleteAsync(ids, before, cancellationToken);

            if (ids.Count < options.RetentionBatchSize)
            {
                break;
            }
        }

        JobLog.Retention(logger, removed);
    }

    private IQueryable<Job> Expired(DateTimeOffset before) =>
        context.Set<Job>().AsNoTracking().Where(job => job.Status == JobStatus.Succeeded && job.CompletedAt < before);

    private async Task<int> DeleteAsync(List<Guid> ids, DateTimeOffset before, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        await context.Set<JobAttempt>()
            .Where(attempt => ids.Contains(attempt.JobId)
                && context.Set<Job>().Any(job => job.Id == attempt.JobId && job.Status == JobStatus.Succeeded && job.CompletedAt < before))
            .ExecuteDeleteAsync(cancellationToken);

        var removed = await context.Set<Job>()
            .Where(job => ids.Contains(job.Id) && job.Status == JobStatus.Succeeded && job.CompletedAt < before)
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
            await scheduler.AddRecurringAsync<JobRetentionJob, JobRetentionPayload>(JobRetentionJob.RecurringName, options.RetentionSchedule, new JobRetentionPayload(), cancellationToken: cancellationToken);
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            JobLog.RetentionScheduleFailed(logger, exception);
            return false;
        }
    }
}
