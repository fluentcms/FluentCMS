using Microsoft.EntityFrameworkCore.Infrastructure;

namespace FluentCMS.Core.Storage;

// The next job due on a queue, leased to one worker until LeaseUntil.
public sealed record JobClaim(string Queue, string Owner, DateTimeOffset Now, DateTimeOffset LeaseUntil);

// The values of cms_job.status that claiming reads and writes.
public static class JobClaimStatus
{
    public const string Pending = "pending";

    public const string Running = "running";
}

// Claims one job from cms_job, so that of any number of concurrent workers exactly one gets each job. A candidate
// is pending or running, due, under max_attempts, and has no lease or an expired one, which is how a job comes back
// after its worker died. The next is the highest priority, then the earliest run_at. The claim sets status to
// running, takes the lease and adds one to attempts. Dates are bound as the provider's conventions store them.
public interface IJobClaimDialect
{
    // The id of the claimed job, or null if there was nothing to claim.
    Task<Guid?> ClaimAsync(DatabaseFacade database, JobClaim claim, CancellationToken cancellationToken = default);
}
