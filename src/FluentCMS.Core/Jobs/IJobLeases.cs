namespace FluentCMS.Core.Jobs;

// How a worker claims and finishes jobs (ADR-0010). Leases replace a broker: a worker holds a job until its lease runs out, and a
// job whose worker stopped is claimed again. Every change after the claim names the owner, so a worker that lost its lease
// changes nothing.
public interface IJobLeases
{
    // The next due job, claimed for the owner with one more attempt counted; null when none is due.
    Task<Job?> ClaimAsync(string owner, TimeSpan duration, CancellationToken cancellationToken = default);

    Task<bool> ReportProgressAsync(Guid jobId, string owner, JobProgress progress, TimeSpan duration, CancellationToken cancellationToken = default);

    // Keeps the lease without touching the progress, for a handler that is still working.
    Task<bool> RenewAsync(Guid jobId, string owner, TimeSpan duration, CancellationToken cancellationToken = default);

    Task<bool> SucceedAsync(Guid jobId, string owner, CancellationToken cancellationToken = default);

    Task<bool> FailAsync(Guid jobId, string owner, string error, CancellationToken cancellationToken = default);

    // Dead at once, for a job no attempt can help, such as one that no handler runs.
    Task<bool> AbandonAsync(Guid jobId, string owner, string error, CancellationToken cancellationToken = default);

    // Queued again after the delay while attempts remain, dead otherwise.
    Task<bool> RetryAsync(Guid jobId, string owner, string error, TimeSpan delay, CancellationToken cancellationToken = default);

    // Queued again at once with the attempt given back, for a job its worker stopped on purpose, such as when the host shuts down.
    Task<bool> ReleaseAsync(Guid jobId, string owner, CancellationToken cancellationToken = default);
}
