namespace FluentCMS.Jobs;

/// <summary>A job that ran out of attempts. Its payload is left out, because it can hold secrets.</summary>
/// <param name="Id">The id of the job.</param>
/// <param name="Type">The name the job is registered under.</param>
/// <param name="Queue">The queue the job is on.</param>
/// <param name="Attempts">The attempts the job has had.</param>
/// <param name="LastError">The error of the last attempt.</param>
/// <param name="DeadLetteredAt">When the job was dead-lettered.</param>
public sealed record DeadLetteredJob(Guid Id, string Type, string Queue, int Attempts, string? LastError, DateTimeOffset? DeadLetteredAt);

/// <summary>Lists the jobs that ran out of attempts and gives them another chance.</summary>
public interface IJobDeadLetters
{
    /// <summary>Lists dead-lettered jobs, the most recent first.</summary>
    /// <param name="offset">The number of jobs to skip.</param>
    /// <param name="limit">The most jobs to return.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The jobs.</returns>
    Task<IReadOnlyList<DeadLetteredJob>> ListAsync(int offset = 0, int limit = 50, CancellationToken cancellationToken = default);

    /// <summary>Queues a dead-lettered job to run now, with as many further attempts as it had before.</summary>
    /// <param name="id">The id of the job.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Whether a dead-lettered job with this id was queued.</returns>
    Task<bool> RetryAsync(Guid id, CancellationToken cancellationToken = default);
}
