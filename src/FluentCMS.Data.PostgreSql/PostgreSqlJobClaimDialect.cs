using FluentCMS.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace FluentCMS.Data.PostgreSql;

// Not yet run on a database (#2711).
public sealed class PostgreSqlJobClaimDialect : IJobClaimDialect
{
    public async Task<Guid?> ClaimAsync(DatabaseFacade database, JobClaim claim, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(claim);

        var now = claim.Now.ToUniversalTime();
        var ids = await database.SqlQuery<Guid>($"""
            UPDATE cms_job
            SET status = {JobClaimStatus.Running}, lease_owner = {claim.Owner}, lease_until = {claim.LeaseUntil.ToUniversalTime()}, attempts = attempts + 1
            WHERE id = (
                SELECT id
                FROM cms_job
                WHERE queue = {claim.Queue}
                    AND status IN ('pending', 'running')
                    AND run_at <= {now}
                    AND attempts < max_attempts
                    AND (lease_until IS NULL OR lease_until < {now})
                ORDER BY priority DESC, run_at, id
                LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING id AS "Value"
            """).ToListAsync(cancellationToken);

        return ids.Count == 0 ? null : ids[0];
    }
}
