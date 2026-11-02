namespace FluentCMS.Jobs;

/// <summary>
/// A unit of background work. An attempt that throws is retried with backoff, so a job should be safe to run again:
/// a worker that dies can leave a job half done.
/// </summary>
/// <typeparam name="TPayload">The job's input, stored as JSON when the job is enqueued.</typeparam>
public interface IJob<TPayload>
{
    /// <summary>Runs one attempt of the job.</summary>
    /// <param name="payload">The input given when the job was enqueued.</param>
    /// <param name="context">The attempt, its operation context and its cancellation token.</param>
    /// <returns>A task that completes when the attempt has succeeded, and faults when it has failed.</returns>
    Task ExecuteAsync(TPayload payload, JobContext context);
}
