using FluentCMS.Core.Identity;

namespace FluentCMS.Host.Jobs;

// The job a scope runs, set by the worker alone. Its initiator is who enqueued the job, so the handler's changes name them.
internal sealed class JobRun
{
    public Initiator? Initiator { get; set; }
}

// Wraps the host's own ICurrentInitiator, the request's or the stdio message's caller, so that inside a job's scope the job's
// initiator is the caller instead (AUD-01).
internal sealed class JobAwareInitiator(JobRun run, ICurrentInitiator host) : ICurrentInitiator
{
    public Initiator? Current => run.Initiator ?? host.Current;
}
