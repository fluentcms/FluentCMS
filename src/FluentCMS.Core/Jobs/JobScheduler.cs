using FluentCMS.Core.Storage;
using FluentCMS.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Jobs;

// Writes through a context of its own, so that enqueuing never saves what a caller has tracked on its own context.
internal sealed class JobScheduler(
    IServiceScopeFactory scopes,
    JobRegistry registry,
    RecurringJobs recurring,
    JobFactory jobs,
    TimeProvider time) : IJobScheduler
{
    public const int TypeMaxLength = 128;
    public const int QueueMaxLength = JobFactory.QueueMaxLength;

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
        var job = jobs.Create<TJob, TPayload>(payload, runAt, jobOptions);

        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        context.Add(job);
        await context.SaveChangesAsync(cancellationToken);

        return job.Id;
    }

    private static string Serialize<TPayload>(TPayload payload) => JobFactory.Serialize(payload);

    private static string CheckedQueue(string? queue) => JobFactory.CheckedQueue(queue);
}
