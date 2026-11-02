using FluentCMS.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace FluentCMS.Data.SqlServer;

// Not yet run on a database (#2711).
public sealed class SqlServerJobClaimDialect : IJobClaimDialect
{
    public async Task<Guid?> ClaimAsync(DatabaseFacade database, JobClaim claim, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(claim);

        var ids = await database.SqlQuery<Guid>($"""
            WITH next_job AS (
                SELECT TOP (1) status, lease_owner, lease_until, attempts, id
                FROM cms_job WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE queue = {claim.Queue}
                    AND status IN ({JobClaimStatus.Pending}, {JobClaimStatus.Running})
                    AND run_at <= {claim.Now}
                    AND attempts < max_attempts
                    AND (lease_until IS NULL OR lease_until < {claim.Now})
                ORDER BY priority DESC, run_at, id)
            UPDATE next_job
            SET status = {JobClaimStatus.Running}, lease_owner = {claim.Owner}, lease_until = {claim.LeaseUntil}, attempts = attempts + 1
            OUTPUT inserted.id AS [Value];
            """).ToListAsync(cancellationToken);

        return ids.Count == 0 ? null : ids[0];
    }
}
