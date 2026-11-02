using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Jobs;

// One attempt of a job, held by one worker.
internal sealed record ClaimedJob(Guid Id, string Type, string Payload, string Queue, Guid? SiteId, int Attempt, int MaxAttempts, string Owner);

// The state changes of cms_job. Each is one conditional update and goes through ExecuteUpdate, because a worker can
// have lost the job to another by now: what it writes then matches no row. Cancellation is not taken, since an outcome
// that is not written means the job runs again.
internal sealed class JobStore(
    IServiceScopeFactory scopes,
    IJobClaimDialect dialect,
    IJobEventSink events,
    JobRunnerOptions options,
    TimeProvider time,
    ILogger<JobStore> logger)
{
    private const int SweepBatch = 100;
    private const string LeaseExpiredError = "The worker stopped without finishing this attempt, and its lease expired.";
    private const string OutOfAttemptsError = "The worker of the last attempt stopped without finishing it, and no attempts are left.";

    public async Task<ClaimedJob?> ClaimAsync(string queue, string owner)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        var now = time.GetUtcNow();
        if (await dialect.ClaimAsync(context.Database, new JobClaim(queue, owner, now, now + options.LeaseDuration)) is not { } id)
        {
            return null;
        }

        var job = await context.Set<Job>().AsNoTracking().SingleAsync(row => row.Id == id);

        // A row still open from an earlier attempt means that attempt's worker died.
        var expired = await context.Set<JobAttempt>()
            .Where(attempt => attempt.JobId == id && attempt.Attempt < job.Attempts && attempt.Outcome == null)
            .ExecuteUpdateAsync(attempt => attempt
                .SetProperty(row => row.Outcome, JobAttemptOutcome.LeaseExpired)
                .SetProperty(row => row.FinishedAt, now)
                .SetProperty(row => row.Error, LeaseExpiredError));

        context.Add(new JobAttempt { JobId = id, Attempt = job.Attempts, StartedAt = now });
        await context.SaveChangesAsync();

        if (expired > 0)
        {
            await PublishAsync(job.Id, new JobFailedEvent(job.Id, job.Type, job.Queue, job.Attempts - 1, job.MaxAttempts, LeaseExpiredError));
        }

        return new ClaimedJob(job.Id, job.Type, job.Payload, job.Queue, job.SiteId, job.Attempts, job.MaxAttempts, owner);
    }

    // False when the worker no longer holds the job.
    public async Task<bool> RenewLeaseAsync(ClaimedJob job)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        var until = time.GetUtcNow() + options.LeaseDuration;
        return await Held(context, job).ExecuteUpdateAsync(row => row.SetProperty(column => column.LeaseUntil, until)) == 1;
    }

    public Task<bool> SucceedAsync(ClaimedJob job)
    {
        var now = time.GetUtcNow();
        return SettleAsync(job, JobAttemptOutcome.Succeeded, error: null, now, row => row
            .SetProperty(column => column.Status, JobStatus.Succeeded)
            .SetProperty(column => column.CompletedAt, now)
            .SetProperty(column => column.LastError, (string?)null)
            .SetProperty(column => column.LeaseOwner, (string?)null)
            .SetProperty(column => column.LeaseUntil, (DateTimeOffset?)null));
    }

    // Retries with backoff while attempts are left, and dead-letters otherwise.
    public async Task<bool> FailAsync(ClaimedJob job, string error)
    {
        var now = time.GetUtcNow();
        var deadLetter = job.Attempt >= job.MaxAttempts;
        var runAt = now + RetryBackoff.After(job.Attempt, options);

        var held = await SettleAsync(job, JobAttemptOutcome.Failed, error, now, row =>
        {
            row.SetProperty(column => column.LastError, error)
                .SetProperty(column => column.LeaseOwner, (string?)null)
                .SetProperty(column => column.LeaseUntil, (DateTimeOffset?)null);

            if (deadLetter)
            {
                row.SetProperty(column => column.Status, JobStatus.DeadLettered)
                    .SetProperty(column => column.CompletedAt, now);
            }
            else
            {
                row.SetProperty(column => column.Status, JobStatus.Pending)
                    .SetProperty(column => column.RunAt, runAt);
            }
        });

        if (held)
        {
            await PublishAsync(job.Id, new JobFailedEvent(job.Id, job.Type, job.Queue, job.Attempt, job.MaxAttempts, error));
            if (deadLetter)
            {
                JobLog.DeadLettered(logger, job.Id, job.Type, job.Attempt, error);
                await PublishAsync(job.Id, new JobDeadLetteredEvent(job.Id, job.Type, job.Queue, job.Attempt, error));
            }
        }

        return held;
    }

    // Hands the job back for another worker. The attempt keeps its row, so it is numbered on, and max_attempts rises
    // by one so that a shutdown does not use up an attempt.
    public Task<bool> ReleaseAsync(ClaimedJob job)
    {
        var now = time.GetUtcNow();
        return SettleAsync(job, JobAttemptOutcome.Released, error: null, now, row => row
            .SetProperty(column => column.Status, JobStatus.Pending)
            .SetProperty(column => column.MaxAttempts, column => column.MaxAttempts + 1)
            .SetProperty(column => column.LeaseOwner, (string?)null)
            .SetProperty(column => column.LeaseUntil, (DateTimeOffset?)null));
    }

    // A claim requires attempts < max_attempts, so a job whose worker died on its last attempt never comes back and
    // would stay running. This dead-letters it. Of several instances sweeping, the conditional update lets one win.
    public async Task<int> SweepAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        var now = time.GetUtcNow();
        var stuck = await Stuck(context, now).AsNoTracking().Take(SweepBatch).ToListAsync();

        var swept = 0;
        foreach (var job in stuck)
        {
            await using var transaction = await context.Database.BeginTransactionAsync();

            var won = await Stuck(context, now).Where(row => row.Id == job.Id).ExecuteUpdateAsync(row => row
                .SetProperty(column => column.Status, JobStatus.DeadLettered)
                .SetProperty(column => column.CompletedAt, now)
                .SetProperty(column => column.LastError, OutOfAttemptsError)
                .SetProperty(column => column.LeaseOwner, (string?)null)
                .SetProperty(column => column.LeaseUntil, (DateTimeOffset?)null));

            if (won == 0)
            {
                continue;
            }

            await context.Set<JobAttempt>()
                .Where(attempt => attempt.JobId == job.Id && attempt.Outcome == null)
                .ExecuteUpdateAsync(attempt => attempt
                    .SetProperty(row => row.Outcome, JobAttemptOutcome.LeaseExpired)
                    .SetProperty(row => row.FinishedAt, now)
                    .SetProperty(row => row.Error, OutOfAttemptsError));

            await transaction.CommitAsync();

            swept++;
            JobLog.DeadLettered(logger, job.Id, job.Type, job.Attempts, OutOfAttemptsError);
            await PublishAsync(job.Id, new JobFailedEvent(job.Id, job.Type, job.Queue, job.Attempts, job.MaxAttempts, OutOfAttemptsError));
            await PublishAsync(job.Id, new JobDeadLetteredEvent(job.Id, job.Type, job.Queue, job.Attempts, OutOfAttemptsError));
        }

        return swept;
    }

    private static IQueryable<Job> Stuck(SystemDbContext context, DateTimeOffset now) =>
        context.Set<Job>().Where(row =>
            (row.Status == JobStatus.Pending || row.Status == JobStatus.Running)
            && row.Attempts >= row.MaxAttempts
            && (row.LeaseUntil == null || row.LeaseUntil < now));

    private static IQueryable<Job> Held(SystemDbContext context, ClaimedJob job) =>
        context.Set<Job>().Where(row =>
            row.Id == job.Id && row.Status == JobStatus.Running && row.LeaseOwner == job.Owner && row.Attempts == job.Attempt);

    private async Task<bool> SettleAsync(ClaimedJob job, string outcome, string? error, DateTimeOffset now, Action<UpdateSettersBuilder<Job>> update)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync();

        if (await Held(context, job).ExecuteUpdateAsync(update) == 0)
        {
            return false;
        }

        await context.Set<JobAttempt>()
            .Where(attempt => attempt.JobId == job.Id && attempt.Attempt == job.Attempt)
            .ExecuteUpdateAsync(attempt => attempt
                .SetProperty(row => row.Outcome, outcome)
                .SetProperty(row => row.FinishedAt, now)
                .SetProperty(row => row.Error, error));

        await transaction.CommitAsync();
        return true;
    }

    private Task PublishAsync(Guid jobId, JobFailedEvent failed) => GuardAsync(jobId, () => events.PublishAsync(failed));

    private Task PublishAsync(Guid jobId, JobDeadLetteredEvent deadLettered) => GuardAsync(jobId, () => events.PublishAsync(deadLettered));

    private async Task GuardAsync(Guid jobId, Func<Task> publish)
    {
        try
        {
            await publish();
        }
        catch (Exception exception)
        {
            JobLog.EventSinkFailed(logger, exception, jobId);
        }
    }
}
