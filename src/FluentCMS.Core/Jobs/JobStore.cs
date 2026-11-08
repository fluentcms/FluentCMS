using FluentCMS.Core.Events;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Jobs;

internal sealed record ClaimedJob(Guid Id, string Type, string Payload, string Queue, Guid? SiteId, int Attempt, int MaxAttempts, string Owner, DateTimeOffset LeaseUntil);

// A worker can have lost its job to another by the time it writes, so every write after the claim is conditional on
// still holding it, and matches no row otherwise. ExecuteUpdate, because SaveChanges uses UPDATE ... RETURNING on SQLite.
internal sealed class JobStore(
    IServiceScopeFactory scopes,
    IJobClaimDialect dialect,
    IJobEventSink events,
    OutboxRows outbox,
    OutboxKick kick,
    JobRunnerOptions options,
    TimeProvider time,
    ILogger<JobStore> logger)
{
    private const int SweepBatch = 100;
    private const string LeaseExpiredError = "The worker stopped without finishing this attempt, and its lease expired.";
    private const string OutOfAttemptsError = "The worker of the last attempt stopped without finishing it, and no attempts are left.";
    private const string CancelledError = "The job was cancelled before it ran.";

    public async Task<ClaimedJob?> ClaimAsync(string queue, string owner, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        var now = time.GetUtcNow();

        // The transaction takes the write lock on SQLite, which an idle poll must not do.
        if (!await AnyDueAsync(context, queue, now, cancellationToken))
        {
            return null;
        }

        // One transaction, so that a failure here does not use up an attempt of a job that never ran.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var leaseUntil = now + options.LeaseDuration;
        if (await dialect.ClaimAsync(context.Database, new JobClaim(queue, owner, now, leaseUntil), cancellationToken) is not { } id)
        {
            return null;
        }

        var job = await context.Set<Job>().AsNoTracking().SingleAsync(row => row.Id == id, cancellationToken);

        // A row still open from an earlier attempt means that attempt's worker died.
        var expired = await context.Set<JobAttempt>()
            .Where(attempt => attempt.JobId == id && attempt.Attempt < job.Attempts && attempt.Outcome == null)
            .ExecuteUpdateAsync(
                attempt => attempt
                    .SetProperty(row => row.Outcome, JobAttemptOutcome.LeaseExpired)
                    .SetProperty(row => row.FinishedAt, now)
                    .SetProperty(row => row.Error, LeaseExpiredError),
                cancellationToken);

        context.Add(new JobAttempt { JobId = id, Attempt = job.Attempts, StartedAt = now });

        // The event is recorded for the outbox handlers in the same commit as the fact it reports.
        var expiredEvent = expired > 0 ? Failed(job.Id, job.SiteId, job.Type, job.Queue, job.Attempts - 1, job.MaxAttempts, LeaseExpiredError) : null;
        var rows = expiredEvent is null ? [] : await outbox.BuildAsync([expiredEvent], cancellationToken);
        context.AddRange(rows);
        await context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(CancellationToken.None);

        if (expiredEvent is not null)
        {
            await PublishAsync(job.Id, expiredEvent);
            await KickAsync(rows.Count);
        }

        return new ClaimedJob(job.Id, job.Type, job.Payload, job.Queue, job.SiteId, job.Attempts, job.MaxAttempts, owner, leaseUntil);
    }

    // The new end of the lease, or null when the worker no longer holds the job.
    public async Task<DateTimeOffset?> RenewLeaseAsync(ClaimedJob job)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        var until = time.GetUtcNow() + options.LeaseDuration;
        var renewed = await Held(context, job).ExecuteUpdateAsync(row => row.SetProperty(column => column.LeaseUntil, until));

        return renewed == 1 ? until : null;
    }

    public Task<bool> SucceedAsync(ClaimedJob job)
    {
        var now = time.GetUtcNow();
        return SettleAsync(job, JobAttemptOutcome.Succeeded, error: null, now, [], row => row
            .SetProperty(column => column.Status, JobStatus.Succeeded)
            .SetProperty(column => column.CompletedAt, now)
            .SetProperty(column => column.LastError, (string?)null)
            .SetProperty(column => column.LeaseOwner, (string?)null)
            .SetProperty(column => column.LeaseUntil, (DateTimeOffset?)null));
    }

    public async Task<bool> FailAsync(ClaimedJob job, string error)
    {
        var now = time.GetUtcNow();
        var deadLetter = job.Attempt >= job.MaxAttempts;
        var runAt = now + RetryBackoff.After(job.Attempt, options);

        var failedEvent = Failed(job.Id, job.SiteId, job.Type, job.Queue, job.Attempt, job.MaxAttempts, error);
        var deadEvent = deadLetter ? DeadLettered(job.Id, job.SiteId, job.Type, job.Queue, job.Attempt, error) : null;
        var rows = await outbox.BuildAsync(deadEvent is null ? [failedEvent] : [failedEvent, deadEvent], CancellationToken.None);

        var held = await SettleAsync(job, JobAttemptOutcome.Failed, error, now, rows, row =>
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
            await PublishAsync(job.Id, failedEvent);
            if (deadEvent is not null)
            {
                JobLog.DeadLettered(logger, job.Id, job.Type, job.Attempt, error);
                await PublishAsync(job.Id, deadEvent);
            }

            await KickAsync(rows.Count);
        }

        return held;
    }

    // The attempt keeps its row, because attempts are numbered on, so max_attempts rises by one to give it back.
    public Task<bool> ReleaseAsync(ClaimedJob job)
    {
        var now = time.GetUtcNow();
        return SettleAsync(job, JobAttemptOutcome.Released, error: null, now, [], row => row
            .SetProperty(column => column.Status, JobStatus.Pending)
            .SetProperty(column => column.MaxAttempts, column => column.MaxAttempts + 1)
            .SetProperty(column => column.LeaseOwner, (string?)null)
            .SetProperty(column => column.LeaseUntil, (DateTimeOffset?)null));
    }

    // A claim requires attempts < max_attempts, so a job whose worker died on its last attempt never comes back and
    // would stay running without this.
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

            var failedEvent = Failed(job.Id, job.SiteId, job.Type, job.Queue, job.Attempts, job.MaxAttempts, OutOfAttemptsError);
            var deadEvent = DeadLettered(job.Id, job.SiteId, job.Type, job.Queue, job.Attempts, OutOfAttemptsError);
            var rows = await outbox.BuildAsync([failedEvent, deadEvent], CancellationToken.None);
            context.AddRange(rows);
            await context.SaveChangesAsync();

            await transaction.CommitAsync();

            swept++;
            JobLog.DeadLettered(logger, job.Id, job.Type, job.Attempts, OutOfAttemptsError);
            await PublishAsync(job.Id, failedEvent);
            await PublishAsync(job.Id, deadEvent);
            await KickAsync(rows.Count);
        }

        return swept;
    }

    // Only a job that no worker holds: it is dead-lettered with its event, as running out of attempts would, so what listens
    // for that, such as the schedule of a publish, closes too, and it can be retried from the dead-letter list.
    public async Task<bool> CancelAsync(Guid id, EventSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var now = time.GetUtcNow();
        var cancelled = await context.Set<Job>()
            .Where(row => row.Id == id && row.Status == JobStatus.Pending)
            .ExecuteUpdateAsync(row => row
                .SetProperty(column => column.Status, JobStatus.DeadLettered)
                .SetProperty(column => column.CompletedAt, now)
                .SetProperty(column => column.LastError, CancelledError),
                cancellationToken);
        if (cancelled == 0)
        {
            return false;
        }

        var job = await context.Set<Job>().AsNoTracking().SingleAsync(row => row.Id == id, cancellationToken);
        var deadEvent = new JobDeadLetteredEvent(job.Id, job.Type, job.Queue, job.Attempts, CancelledError) { Source = source with { SiteId = job.SiteId ?? Guid.Empty } };
        var rows = await outbox.BuildAsync([deadEvent], cancellationToken);
        context.AddRange(rows);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(CancellationToken.None);

        await PublishAsync(job.Id, deadEvent);
        await KickAsync(rows.Count);
        return true;
    }

    // The run_at of the oldest job of each queue that a worker could claim now, whether or not any instance serves it.
    public async Task<Dictionary<string, DateTimeOffset>> OldestDueAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        return await Due(context, time.GetUtcNow())
            .GroupBy(row => row.Queue)
            .Select(queue => new { Queue = queue.Key, Oldest = queue.Min(row => row.RunAt) })
            .ToDictionaryAsync(queue => queue.Queue, queue => queue.Oldest, StringComparer.Ordinal, cancellationToken);
    }

    private static Task<bool> AnyDueAsync(SystemDbContext context, string queue, DateTimeOffset now, CancellationToken cancellationToken) =>
        Due(context, now).AnyAsync(row => row.Queue == queue, cancellationToken);

    private static IQueryable<Job> Due(SystemDbContext context, DateTimeOffset now) =>
        context.Set<Job>().AsNoTracking().Where(row =>
            (row.Status == JobStatus.Pending || row.Status == JobStatus.Running)
            && row.RunAt <= now
            && row.Attempts < row.MaxAttempts
            && (row.LeaseUntil == null || row.LeaseUntil < now));

    private static IQueryable<Job> Stuck(SystemDbContext context, DateTimeOffset now) =>
        context.Set<Job>().Where(row =>
            (row.Status == JobStatus.Pending || row.Status == JobStatus.Running)
            && row.Attempts >= row.MaxAttempts
            && (row.LeaseUntil == null || row.LeaseUntil < now));

    private static IQueryable<Job> Held(SystemDbContext context, ClaimedJob job) =>
        context.Set<Job>().Where(row =>
            row.Id == job.Id && row.Status == JobStatus.Running && row.LeaseOwner == job.Owner && row.Attempts == job.Attempt);

    private async Task<bool> SettleAsync(ClaimedJob job, string outcome, string? error, DateTimeOffset now, List<OutboxMessage> rows, Action<UpdateSettersBuilder<Job>> update)
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

        // The events of the outcome are recorded in the commit that makes it true, or not at all.
        if (rows.Count > 0)
        {
            context.AddRange(rows);
            await context.SaveChangesAsync();
        }

        await transaction.CommitAsync();
        return true;
    }

    // The ids follow from the job and the attempt, so that an outcome written again after a failed write is the same event.
    private JobFailedEvent Failed(Guid id, Guid? site, string type, string queue, int attempt, int maxAttempts, string error) =>
        new(id, type, queue, attempt, maxAttempts, SecretScrubber.Of(error))
        {
            EventId = OutboxRows.StableId($"job:{id}:{attempt}:failed"),
            Source = SourceOf(site),
        };

    private JobDeadLetteredEvent DeadLettered(Guid id, Guid? site, string type, string queue, int attempts, string error) =>
        new(id, type, queue, attempts, SecretScrubber.Of(error))
        {
            EventId = OutboxRows.StableId($"job:{id}:{attempts}:dead"),
            Source = SourceOf(site),
        };

    // The system raises it, on the site the job belongs to.
    private EventSource SourceOf(Guid? site) => EventSource.FromSystem(time.GetUtcNow()) with { SiteId = site ?? Guid.Empty };

    private async Task KickAsync(int rows)
    {
        if (rows > 0)
        {
            await kick.KickAsync();
        }
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
