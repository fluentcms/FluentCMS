namespace FluentCMS.Jobs;

/// <summary>
/// Adds jobs to the queue. A job is written on its own, outside any transaction of the caller, so it can start before
/// the caller's transaction commits.
/// </summary>
public interface IJobScheduler
{
    /// <summary>Enqueues a job to run as soon as a worker is free.</summary>
    /// <typeparam name="TJob">The job, which must be registered.</typeparam>
    /// <typeparam name="TPayload">The job's input.</typeparam>
    /// <param name="payload">The input, which must not be <see langword="null"/>.</param>
    /// <param name="options">How to run the job, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The id of the job.</returns>
    Task<Guid> EnqueueAsync<TJob, TPayload>(TPayload payload, JobOptions? options = null, CancellationToken cancellationToken = default)
        where TJob : class, IJob<TPayload>;

    /// <summary>Enqueues a job that runs no earlier than a given time.</summary>
    /// <typeparam name="TJob">The job, which must be registered.</typeparam>
    /// <typeparam name="TPayload">The job's input.</typeparam>
    /// <param name="payload">The input, which must not be <see langword="null"/>.</param>
    /// <param name="runAt">The earliest time to run the job.</param>
    /// <param name="options">How to run the job, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The id of the job.</returns>
    Task<Guid> ScheduleAsync<TJob, TPayload>(TPayload payload, DateTimeOffset runAt, JobOptions? options = null, CancellationToken cancellationToken = default)
        where TJob : class, IJob<TPayload>;

    /// <summary>
    /// Adds a job that fires on a schedule, once across all instances, or updates the one of that name. Keeping the
    /// same cron keeps the next run, so calling this at every startup does not move the schedule.
    /// </summary>
    /// <typeparam name="TJob">The job, which must be registered.</typeparam>
    /// <typeparam name="TPayload">The job's input.</typeparam>
    /// <param name="name">The name of the schedule.</param>
    /// <param name="cron">A five-field cron expression, read as UTC.</param>
    /// <param name="payload">The input of every run, which must not be <see langword="null"/>.</param>
    /// <param name="queue">The queue, or <see langword="null"/> for the default queue.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the schedule is stored.</returns>
    /// <exception cref="ArgumentException">The cron expression is not valid, or never occurs.</exception>
    Task AddRecurringAsync<TJob, TPayload>(string name, string cron, TPayload payload, string? queue, CancellationToken cancellationToken = default)
        where TJob : class, IJob<TPayload>;
}
