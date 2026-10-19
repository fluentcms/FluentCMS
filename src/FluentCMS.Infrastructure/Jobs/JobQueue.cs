using System.Text.Json;
using FluentCMS.Core.Audit;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Results;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Jobs;

internal sealed class JobQueue(FluentCmsDbContext context, ICurrentInitiator currentInitiator, TimeProvider clock) : IJobQueue
{
    public Task<Job> EnqueueAsync(NewJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (string.IsNullOrWhiteSpace(job.Type) || job.Type.Length > JobConfiguration.MaxTypeLength)
        {
            throw new ArgumentException($"A job type is a name of at most {JobConfiguration.MaxTypeLength} characters, and '{job.Type}' is not.", nameof(job));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(job.MaxAttempts, 1, nameof(job));

        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Jobs are enqueued inside a unit of work, in the same transaction as the change that needs them (ADR-0010).");
        }

        var now = clock.GetUtcNow();
        var initiator = currentInitiator.Current;
        (string Kind, Guid Id)? described = initiator is null ? null : InitiatorKinds.Describe(initiator);

        var row = new JobRow
        {
            Id = Guid.CreateVersion7(now),
            Type = job.Type,
            Payload = job.Payload.ValueKind == JsonValueKind.Undefined ? "null" : job.Payload.GetRawText(),
            SubjectType = job.Subject?.Type,
            SubjectId = job.Subject?.Id,
            InitiatorKind = described?.Kind,
            InitiatorId = described?.Id,
            InitiatorName = initiator?.DisplayName,
            State = JobState.Queued,
            MaxAttempts = job.MaxAttempts,
            CreatedAt = now,
            ScheduledAt = (job.RunAt ?? now).ToUniversalTime(),
        };

        // The row reaches the database with the caller's next save inside the transaction, so it commits with the change or not at all.
        context.Jobs.Add(row);
        return Task.FromResult(row.ToJob());
    }

    public async Task<Result<Job>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Jobs.AsNoTracking().SingleOrDefaultAsync(job => job.Id == id, cancellationToken);

        return row is null
            ? Result<Job>.Failure(JobErrorCodes.JobNotFound, $"There is no job {id}.")
            : Result<Job>.Success(row.ToJob());
    }

    public async Task<IReadOnlyList<Job>> ListForSubjectAsync(JobSubject subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var rows = await context.Jobs
            .AsNoTracking()
            .Where(job => job.SubjectType == subject.Type && job.SubjectId == subject.Id)
            .ToListAsync(cancellationToken);

        // Ordered here because EF Core will not order a DateTimeOffset on SQLite; an item has few jobs.
        return [.. rows.OrderByDescending(job => job.CreatedAt).ThenByDescending(job => job.Id).Select(job => job.ToJob())];
    }
}
