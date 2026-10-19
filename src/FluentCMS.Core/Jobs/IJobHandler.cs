using FluentCMS.Core.Extensions;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Jobs;

// Runs the jobs of one type (ADR-0010, EXT-01). The worker runs it in a scope of its own as the job's initiator. A returned
// failure ends the job as failed; an exception is retried after a growing delay until the job's attempts run out, so a handler
// throws only for what another attempt can fix, and its work must be safe to repeat.
[ExtensionPoint]
public interface IJobHandler
{
    // Stored in every job of the type, so it never changes once released.
    string Type { get; }

    // The engine the type's jobs run as, whoever enqueued them, for work no person or agent does, such as the check run a submission
    // starts (ADR-0012). The job still records who enqueued it. Null runs each job as whoever enqueued it.
    SystemInitiator? RunAs => null;

    Task<Result> RunAsync(JobContext context, CancellationToken cancellationToken);
}

public sealed class JobContext(Job job, IServiceProvider services, Func<JobProgress, CancellationToken, Task<bool>> report)
{
    public Job Job { get; } = job;

    public IServiceProvider Services { get; } = services;

    // Writes progress to the job, where tools and the desk read it, and keeps the lease. False when the lease was lost: another
    // worker has the job, so the handler should stop.
    public Task<bool> ReportProgressAsync(int done, int? total, string? message, CancellationToken cancellationToken = default)
    {
        return report(new JobProgress(done, total, message), cancellationToken);
    }
}
