using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using FluentCMS.Jobs;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Jobs;

internal sealed record JobInfo(
    Guid Id,
    string Type,
    string Queue,
    Guid? SiteId,
    string Status,
    int Attempts,
    int MaxAttempts,
    int Priority,
    DateTimeOffset RunAt,
    string? LastError,
    DateTimeOffset CreatedAt,
    Guid? CreatedBy,
    Origin Origin,
    DateTimeOffset? CompletedAt);

internal sealed record JobAttemptInfo(int Attempt, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string? Outcome, string? Error);

internal sealed record JobDetail(JobInfo Job, IReadOnlyList<JobAttemptInfo> Attempts);

internal sealed record JobFilter(string? Status, string? Type, string? Queue);

internal sealed record JobPage(IReadOnlyList<JobInfo> Items, long Total, int Page, int PageSize);

// The jobs of the instance for the Management API, with jobs.manage for the instance: a job's error can name internals of
// the host whatever site it ran for. A payload is never shown, since it can hold secrets.
internal sealed class JobAdministration(
    SystemDbContext context,
    IJobDeadLetters deadLetters,
    JobStore store,
    JobExecutor executor,
    JobRunnerOptions options,
    ManagementAuthority authority,
    IOperationContextAccessor accessor,
    TimeProvider time)
{
    public static readonly string[] Statuses = [JobStatus.Pending, JobStatus.Running, JobStatus.Succeeded, JobStatus.DeadLettered];

    public async Task<JobPage> ListAsync(JobFilter filter, int page, int pageSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        await RequireAsync(cancellationToken);

        var jobs = context.Set<Job>().AsNoTracking();
        if (filter.Status is { } status)
        {
            jobs = jobs.Where(job => job.Status == status);
        }

        if (filter.Type is { } type)
        {
            jobs = jobs.Where(job => job.Type == type);
        }

        if (filter.Queue is { } queue)
        {
            jobs = jobs.Where(job => job.Queue == queue);
        }

        var total = await jobs.LongCountAsync(cancellationToken);
        var found = await jobs.OrderByDescending(job => job.CreatedAt).ThenByDescending(job => job.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return new JobPage([.. found.Select(Info)], total, page, pageSize);
    }

    public async Task<JobDetail> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);

        var job = await context.Set<Job>().AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw NotFound(id);
        var attempts = await context.Set<JobAttempt>().AsNoTracking().Where(attempt => attempt.JobId == id).ToListAsync(cancellationToken);
        return new JobDetail(Info(job), [.. attempts.OrderBy(attempt => attempt.Attempt).Select(attempt => new JobAttemptInfo(attempt.Attempt, attempt.StartedAt, attempt.FinishedAt, attempt.Outcome, attempt.Error))]);
    }

    public async Task<IReadOnlyList<DeadLetteredJob>> DeadLettersAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        return await deadLetters.ListAsync((page - 1) * pageSize, pageSize, cancellationToken);
    }

    public async Task<JobDetail> RetryAsync(Guid id, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        if (!await deadLetters.RetryAsync(id, cancellationToken))
        {
            throw await RefusedAsync(id, JobErrorCodes.NotDeadLettered, "Only a dead-lettered job is retried.", cancellationToken);
        }

        return await GetAsync(id, cancellationToken);
    }

    public async Task<JobDetail> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);

        var source = accessor.Current is { } operation ? EventSource.From(operation) : EventSource.FromSystem(time.GetUtcNow());
        if (!await store.CancelAsync(id, source, cancellationToken))
        {
            throw await RefusedAsync(id, JobErrorCodes.NotCancellable, "Only a pending job is cancelled; one that is running is left to finish.", cancellationToken);
        }

        return await GetAsync(id, cancellationToken);
    }

    // The names of the recurring jobs, which are what `jobs run` takes: each has the payload and queue of its runs.
    public async Task<IReadOnlyList<string>> RecurringNamesAsync(CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        var names = await context.Set<RecurringJob>().AsNoTracking().Select(job => job.Name).ToListAsync(cancellationToken);
        return [.. names.Order(StringComparer.Ordinal)];
    }

    // A run of the recurring job of this name, due now, with its payload and queue; null when there is none. Queued, it is
    // the next run of whichever instance works its queue. Inline, it is held by this process from the start and runs here to
    // its end with one attempt, so that a failure is dead-lettered rather than left for an instance that may never start.
    public async Task<JobDetail?> RunRecurringAsync(string name, bool inline, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        await RequireAsync(cancellationToken);

        if (await context.Set<RecurringJob>().AsNoTracking().FirstOrDefaultAsync(job => job.Name == name, cancellationToken) is not { } recurring)
        {
            return null;
        }

        var now = time.GetUtcNow();
        var current = accessor.Current;
        var job = new Job
        {
            Id = Guid.CreateVersion7(),
            Type = recurring.Type,
            Payload = recurring.Payload,
            Queue = recurring.Queue,
            RunAt = now,
            MaxAttempts = inline ? 1 : options.DefaultMaxAttempts,
            CreatedAt = now,
            CreatedBy = current?.AuditPrincipal.Id is { } author && author != Guid.Empty ? author : null,
            Origin = current?.Origin ?? Origin.Job,
        };

        if (inline)
        {
            var claimed = await store.AddClaimedAsync(job, $"inline:{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}", cancellationToken);
            await executor.RunAsync(claimed, shutdown: cancellationToken);

            // A run stopped by its caller was handed back as pending, which the caller is told.
            return await GetAsync(job.Id, CancellationToken.None);
        }

        context.Add(job);
        await context.SaveChangesAsync(cancellationToken);
        return await GetAsync(job.Id, cancellationToken);
    }

    private ValueTask RequireAsync(CancellationToken cancellationToken) =>
        authority.RequireAsync(CorePermissions.JobsManage, site: null, cancellationToken);

    private async Task<ValidationException> RefusedAsync(Guid id, string code, string message, CancellationToken cancellationToken) =>
        await context.Set<Job>().AnyAsync(job => job.Id == id, cancellationToken)
            ? new ValidationException([new ValidationError("id", code, message)])
            : NotFound(id);

    private static ValidationException NotFound(Guid id) => new([new ValidationError("id", JobErrorCodes.JobNotFound, $"There is no job {id:D}.")]);

    private static JobInfo Info(Job job) =>
        new(job.Id, job.Type, job.Queue, job.SiteId, job.Status, job.Attempts, job.MaxAttempts, job.Priority, job.RunAt, job.LastError, job.CreatedAt, job.CreatedBy, job.Origin, job.CompletedAt);
}
