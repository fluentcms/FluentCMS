using FluentCMS.Core.Results;

namespace FluentCMS.Core.Jobs;

// The durable queue from ADR-0010. A job is enqueued inside the unit of work of the change that needs it, so a job cannot exist
// without its change or the other way round; outside a unit of work EnqueueAsync throws, as the audit log does.
public interface IJobQueue
{
    // The job comes back queued, as the current initiator's; it reaches the database when the caller's unit of work commits.
    Task<Job> EnqueueAsync(NewJob job, CancellationToken cancellationToken = default);

    Task<Result<Job>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    // Newest first.
    Task<IReadOnlyList<Job>> ListForSubjectAsync(JobSubject subject, CancellationToken cancellationToken = default);
}
