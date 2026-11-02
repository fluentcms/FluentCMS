using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Jobs;

// Writes through a context of its own, so that enqueuing never saves what a caller has tracked on its own context.
internal sealed class JobScheduler(
    IServiceScopeFactory scopes,
    JobRegistry registry,
    RecurringJobs recurring,
    IOperationContextAccessor operation,
    JobRunnerOptions options,
    TimeProvider time) : IJobScheduler
{
    public const int TypeMaxLength = 128;
    public const int QueueMaxLength = 64;

    public Task<Guid> EnqueueAsync<TJob, TPayload>(TPayload payload, JobOptions? jobOptions = null, CancellationToken cancellationToken = default)
        where TJob : class, IJob<TPayload> =>
        AddAsync<TJob, TPayload>(payload, time.GetUtcNow(), jobOptions, cancellationToken);

    public Task<Guid> ScheduleAsync<TJob, TPayload>(TPayload payload, DateTimeOffset runAt, JobOptions? jobOptions = null, CancellationToken cancellationToken = default)
        where TJob : class, IJob<TPayload> =>
        AddAsync<TJob, TPayload>(payload, runAt, jobOptions, cancellationToken);

    public Task AddRecurringAsync<TJob, TPayload>(string name, string cron, TPayload payload, string? queue = null, CancellationToken cancellationToken = default)
        where TJob : class, IJob<TPayload>
    {
        ArgumentNullException.ThrowIfNull(payload);

        return recurring.AddAsync(name, cron, registry.TypeOf<TJob, TPayload>(), Serialize(payload), CheckedQueue(queue), cancellationToken);
    }

    private async Task<Guid> AddAsync<TJob, TPayload>(TPayload payload, DateTimeOffset runAt, JobOptions? jobOptions, CancellationToken cancellationToken)
        where TJob : class, IJob<TPayload>
    {
        ArgumentNullException.ThrowIfNull(payload);

        var maxAttempts = jobOptions?.MaxAttempts ?? options.DefaultMaxAttempts;
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1, nameof(jobOptions));

        var current = operation.Current;
        var job = new Job
        {
            Type = registry.TypeOf<TJob, TPayload>(),
            Payload = Serialize(payload),
            Queue = CheckedQueue(jobOptions?.Queue),
            SiteId = (jobOptions?.Site ?? current?.Site)?.Value,
            RunAt = runAt,
            Priority = jobOptions?.Priority ?? 0,
            MaxAttempts = maxAttempts,
            CreatedAt = time.GetUtcNow(),
            CreatedBy = current?.AuditPrincipal.Id is { } author && author != Guid.Empty ? author : null,
            Origin = current?.Origin ?? Origin.Job,
        };

        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        context.Add(job);
        await context.SaveChangesAsync(cancellationToken);

        return job.Id;
    }

    private static string Serialize<TPayload>(TPayload payload) => JsonSerializer.Serialize(payload, JobJson.Options);

    private static string CheckedQueue(string? queue)
    {
        queue ??= JobRunnerOptions.DefaultQueue;
        ArgumentException.ThrowIfNullOrEmpty(queue);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(queue.Length, QueueMaxLength, nameof(queue));

        return queue;
    }
}
