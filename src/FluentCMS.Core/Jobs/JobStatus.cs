using FluentCMS.Core.Storage;

namespace FluentCMS.Core.Jobs;

// The values of cms_job.status.
public static class JobStatus
{
    public const string Pending = JobClaimStatus.Pending;

    public const string Running = JobClaimStatus.Running;

    public const string Succeeded = "succeeded";

    public const string DeadLettered = "dead_lettered";
}

// The values of cms_job_attempt.outcome.
public static class JobAttemptOutcome
{
    public const string Succeeded = "succeeded";

    public const string Failed = "failed";

    // The worker went away and its lease ran out.
    public const string LeaseExpired = "lease_expired";

    // A shutdown handed the job back; this does not use up an attempt.
    public const string Released = "released";
}
