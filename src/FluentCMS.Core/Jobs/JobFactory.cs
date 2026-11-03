using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Jobs;

namespace FluentCMS.Core.Jobs;

// Builds the cms_job row of a job without saving it, so that a caller with a transaction of its own can write the row in it.
internal sealed class JobFactory(JobRegistry registry, IOperationContextAccessor operation, JobRunnerOptions options, TimeProvider time)
{
    public const int QueueMaxLength = 64;

    public Job Create<TJob, TPayload>(TPayload payload, DateTimeOffset runAt, JobOptions? jobOptions)
        where TJob : class, IJob<TPayload>
    {
        ArgumentNullException.ThrowIfNull(payload);

        var maxAttempts = jobOptions?.MaxAttempts ?? options.DefaultMaxAttempts;
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1, nameof(jobOptions));

        var current = operation.Current;
        return new Job
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
    }

    public static string Serialize<TPayload>(TPayload payload) => JsonSerializer.Serialize(payload, JobJson.Options);

    public static string CheckedQueue(string? queue)
    {
        queue ??= JobRunnerOptions.DefaultQueue;
        ArgumentException.ThrowIfNullOrEmpty(queue);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(queue.Length, QueueMaxLength, nameof(queue));

        return queue;
    }
}
