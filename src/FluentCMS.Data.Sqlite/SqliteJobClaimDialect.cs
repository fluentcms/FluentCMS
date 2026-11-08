using FluentCMS.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace FluentCMS.Data.Sqlite;

// Optimistic: read the next job, then update it only if it can still be claimed. A worker that loses the race updates
// nothing and reads again. The two steps are separate because UPDATE ... RETURNING commits on a later step than the
// one Microsoft.Data.Sqlite retries while the database is busy, so under load it throws 'database is locked'.
public sealed class SqliteJobClaimDialect : IJobClaimDialect
{
    private const int MaxTries = 32;

    public async Task<Guid?> ClaimAsync(DatabaseFacade database, JobClaim claim, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(claim);

        var now = claim.Now.UtcTicks;
        var unreadable = 0;

        for (var tries = 0; tries < MaxTries; tries++)
        {
            var candidates = await database.SqlQuery<string>($"""
                SELECT id AS "Value"
                FROM cms_job
                WHERE queue = {claim.Queue}
                    AND status IN ('pending', 'running')
                    AND run_at <= {now}
                    AND attempts < max_attempts
                    AND (lease_until IS NULL OR lease_until <= {now})
                ORDER BY priority DESC, run_at, id
                LIMIT 1 OFFSET {unreadable}
                """).ToListAsync(cancellationToken);

            if (candidates.Count == 0)
            {
                return null;
            }

            // A row not written by EF must not stop the queue, so it is skipped if it holds no id at all.
            if (!Guid.TryParse(candidates[0], out var id))
            {
                unreadable++;
                continue;
            }

            // Matches the text as stored, and stores it as EF would, because everything after the claim binds the
            // id as uppercase text. Every condition is checked again, not only the lease: between the two steps
            // another worker can have finished the job or failed it to a later run_at, and both clear the lease.
            var claimed = await database.ExecuteSqlAsync($"""
                UPDATE cms_job
                SET id = {id.ToString().ToUpperInvariant()}, status = {JobClaimStatus.Running}, lease_owner = {claim.Owner}, lease_until = {claim.LeaseUntil.UtcTicks}, attempts = attempts + 1
                WHERE id = {candidates[0]}
                    AND status IN ('pending', 'running')
                    AND run_at <= {now}
                    AND attempts < max_attempts
                    AND (lease_until IS NULL OR lease_until <= {now})
                """, cancellationToken);

            if (claimed == 1)
            {
                return id;
            }
        }

        return null;
    }
}
