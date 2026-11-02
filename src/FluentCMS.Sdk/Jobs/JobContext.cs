using FluentCMS.Content;

namespace FluentCMS.Jobs;

/// <summary>What a job knows about the attempt it is running.</summary>
public sealed class JobContext
{
    /// <summary>Creates the context of an attempt.</summary>
    /// <param name="jobId">The id of the job.</param>
    /// <param name="type">The name the job is registered under.</param>
    /// <param name="queue">The queue the job is on.</param>
    /// <param name="attempt">The number of this attempt, starting at one.</param>
    /// <param name="maxAttempts">The attempts the job may have before it is dead-lettered.</param>
    /// <param name="operation">The operation context the attempt runs under.</param>
    /// <param name="cancellationToken">Cancelled when the attempt should stop.</param>
    public JobContext(Guid jobId, string type, string queue, int attempt, int maxAttempts, OperationContext operation, CancellationToken cancellationToken)
    {
        JobId = jobId;
        Type = type;
        Queue = queue;
        Attempt = attempt;
        MaxAttempts = maxAttempts;
        Operation = operation;
        CancellationToken = cancellationToken;
    }

    /// <summary>The id of the job.</summary>
    public Guid JobId { get; }

    /// <summary>The name the job is registered under.</summary>
    public string Type { get; }

    /// <summary>The queue the job is on.</summary>
    public string Queue { get; }

    /// <summary>The number of this attempt, starting at one.</summary>
    public int Attempt { get; }

    /// <summary>The attempts the job may have before it is dead-lettered.</summary>
    public int MaxAttempts { get; }

    /// <summary>
    /// The operation context the attempt runs under, which is also <see cref="IOperationContextAccessor.Current"/>.
    /// Its principal is <see cref="Principal.System"/> and its origin <see cref="Origin.Job"/>.
    /// </summary>
    public OperationContext Operation { get; }

    /// <summary>
    /// Cancelled when the host is shutting down and its grace period has passed, or when another worker has taken
    /// over the job. A job that throws <see cref="OperationCanceledException"/> because of this has not failed.
    /// </summary>
    public CancellationToken CancellationToken { get; }
}
