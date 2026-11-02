using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Jobs;

internal sealed class JobDeadLetters(IServiceScopeFactory scopes, TimeProvider time) : IJobDeadLetters
{
    public async Task<IReadOnlyList<DeadLetteredJob>> ListAsync(int offset = 0, int limit = 50, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        return await context.Set<Job>().AsNoTracking()
            .Where(job => job.Status == JobStatus.DeadLettered)
            .OrderByDescending(job => job.CompletedAt)
            .ThenBy(job => job.Id)
            .Skip(offset)
            .Take(limit)
            .Select(job => new DeadLetteredJob(job.Id, job.Type, job.Queue, job.Attempts, job.LastError, job.CompletedAt))
            .ToListAsync(cancellationToken);
    }

    // Attempts count on rather than start over, because cms_job_attempt keeps the rows of the earlier ones.
    public async Task<bool> RetryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        var now = time.GetUtcNow();
        var queued = await context.Set<Job>()
            .Where(job => job.Id == id && job.Status == JobStatus.DeadLettered)
            .ExecuteUpdateAsync(
                job => job
                    .SetProperty(row => row.Status, JobStatus.Pending)
                    .SetProperty(row => row.RunAt, now)
                    .SetProperty(row => row.MaxAttempts, row => row.Attempts + row.MaxAttempts)
                    .SetProperty(row => row.CompletedAt, (DateTimeOffset?)null),
                cancellationToken);

        return queued == 1;
    }
}
