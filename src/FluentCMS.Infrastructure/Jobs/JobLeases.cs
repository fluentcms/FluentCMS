using FluentCMS.Core.Jobs;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Jobs;

// Every change is one conditional update outside any unit of work: a claim applies only to the revision it read, and every later
// change only while the owner still holds the lease. Each moves the revision on, so a renewed lease is never claimed from a
// stale read.
internal sealed class JobLeases(FluentCmsDbContext context, TimeProvider clock) : IJobLeases
{
    private const int MaxErrorLength = 4000;
    private const int CandidateCount = 10;

    public async Task<Job?> ClaimAsync(string owner, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        var now = clock.GetUtcNow();

        // SQL rather than LINQ because EF Core will not compare a DateTimeOffset on SQLite, which stores it as text; every stored
        // time is UTC, so the text compares in time order on both providers, as in AuditReader.
        var candidates = await context.Jobs
            .FromSql($"""
                SELECT * FROM "Jobs"
                WHERE ("State" = 'Queued' AND "ScheduledAt" <= {now}) OR ("State" = 'Running' AND "LeaseUntil" < {now})
                ORDER BY "ScheduledAt"
                LIMIT {CandidateCount}
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            // A worker that stopped on the last attempt leaves a lease that runs out with no attempt left.
            if (candidate.Attempts >= candidate.MaxAttempts)
            {
                await UpdateAsync(
                    candidate,
                    row => row.Id == candidate.Id && row.Revision == candidate.Revision,
                    setters => setters
                        .SetProperty(row => row.State, JobState.Dead)
                        .SetProperty(row => row.FinishedAt, now)
                        .SetProperty(row => row.LeaseOwner, (string?)null)
                        .SetProperty(row => row.LeaseUntil, (DateTimeOffset?)null)
                        .SetProperty(row => row.LastError, $"The lease ran out on attempt {candidate.Attempts} of {candidate.MaxAttempts}; the worker stopped before finishing."),
                    cancellationToken);
                continue;
            }

            var claimed = await UpdateAsync(
                candidate,
                row => row.Id == candidate.Id && row.Revision == candidate.Revision,
                setters => setters
                    .SetProperty(row => row.State, JobState.Running)
                    .SetProperty(row => row.Attempts, candidate.Attempts + 1)
                    .SetProperty(row => row.StartedAt, candidate.StartedAt ?? now)
                    .SetProperty(row => row.LeaseOwner, owner)
                    .SetProperty(row => row.LeaseUntil, now + duration),
                cancellationToken);

            if (claimed)
            {
                candidate.State = JobState.Running;
                candidate.Attempts++;
                candidate.StartedAt ??= now;
                return candidate.ToJob();
            }
        }

        return null;
    }

    public Task<bool> ReportProgressAsync(Guid jobId, string owner, JobProgress progress, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var message = Truncate(progress.Message, JobConfiguration.MaxMessageLength);
        return UpdateHeldAsync(
            jobId,
            owner,
            setters => setters
                .SetProperty(row => row.ProgressDone, progress.Done)
                .SetProperty(row => row.ProgressTotal, progress.Total)
                .SetProperty(row => row.ProgressMessage, message)
                .SetProperty(row => row.LeaseUntil, clock.GetUtcNow() + duration),
            cancellationToken);
    }

    public Task<bool> RenewAsync(Guid jobId, string owner, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        return UpdateHeldAsync(jobId, owner, setters => setters.SetProperty(row => row.LeaseUntil, clock.GetUtcNow() + duration), cancellationToken);
    }

    public Task<bool> SucceedAsync(Guid jobId, string owner, CancellationToken cancellationToken = default)
    {
        return FinishAsync(jobId, owner, JobState.Succeeded, null, cancellationToken);
    }

    public Task<bool> FailAsync(Guid jobId, string owner, string error, CancellationToken cancellationToken = default)
    {
        return FinishAsync(jobId, owner, JobState.Failed, error, cancellationToken);
    }

    public Task<bool> AbandonAsync(Guid jobId, string owner, string error, CancellationToken cancellationToken = default)
    {
        return FinishAsync(jobId, owner, JobState.Dead, error, cancellationToken);
    }

    public async Task<bool> RetryAsync(Guid jobId, string owner, string error, TimeSpan delay, CancellationToken cancellationToken = default)
    {
        var held = await context.Jobs
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == jobId && row.LeaseOwner == owner && row.State == JobState.Running, cancellationToken);
        if (held is null)
        {
            return false;
        }

        if (held.Attempts >= held.MaxAttempts)
        {
            return await FinishAsync(jobId, owner, JobState.Dead, error, cancellationToken);
        }

        var lastError = Truncate(error, MaxErrorLength);
        return await UpdateHeldAsync(
            jobId,
            owner,
            setters => setters
                .SetProperty(row => row.State, JobState.Queued)
                .SetProperty(row => row.ScheduledAt, clock.GetUtcNow() + delay)
                .SetProperty(row => row.LeaseOwner, (string?)null)
                .SetProperty(row => row.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(row => row.LastError, lastError),
            cancellationToken);
    }

    private Task<bool> FinishAsync(Guid jobId, string owner, JobState state, string? error, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var lastError = Truncate(error, MaxErrorLength);

        return UpdateHeldAsync(
            jobId,
            owner,
            setters => setters
                .SetProperty(row => row.State, state)
                .SetProperty(row => row.FinishedAt, now)
                .SetProperty(row => row.LeaseOwner, (string?)null)
                .SetProperty(row => row.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(row => row.LastError, lastError),
            cancellationToken);
    }

    private async Task<bool> UpdateHeldAsync(
        Guid jobId,
        string owner,
        Action<Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<JobRow>> set,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        var updated = await context.Jobs
            .Where(row => row.Id == jobId && row.LeaseOwner == owner && row.State == JobState.Running)
            .ExecuteUpdateAsync(
                setters =>
                {
                    set(setters);
                    setters.SetProperty(row => row.Revision, row => row.Revision + 1);
                },
                cancellationToken);

        return updated == 1;
    }

    private async Task<bool> UpdateAsync(
        JobRow candidate,
        System.Linq.Expressions.Expression<Func<JobRow, bool>> where,
        Action<Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<JobRow>> set,
        CancellationToken cancellationToken)
    {
        var updated = await context.Jobs
            .Where(where)
            .ExecuteUpdateAsync(
                setters =>
                {
                    set(setters);
                    setters.SetProperty(row => row.Revision, candidate.Revision + 1);
                },
                cancellationToken);

        return updated == 1;
    }

    private static string? Truncate(string? text, int length)
    {
        return text is { Length: > 0 } && text.Length > length ? text[..length] : text;
    }
}
