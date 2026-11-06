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
