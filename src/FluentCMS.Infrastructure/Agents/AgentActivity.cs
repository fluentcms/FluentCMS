using FluentCMS.Core.Agents;
using FluentCMS.Infrastructure.ChangeSets;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Agents;

// Counted from the audit log, which records every change set an agent opens with its credential (AUD-01).
internal sealed class AgentActivity(FluentCmsDbContext context) : IAgentActivity
{
    // SQL rather than LINQ because EF Core will not compare a DateTimeOffset on SQLite, which stores it as text; stored
    // timestamps are all UTC, so the text compares in time order on both providers, as in AuditReader.
    public Task<int> ChangeSetsOpenedSinceAsync(Guid credentialId, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        return context.AuditEvents
            .FromSql($"""
                SELECT * FROM "AuditEvents"
                WHERE "Operation" = {ChangeSetDirectory.OpenedOperation} AND "AgentCredentialId" = {credentialId} AND "Timestamp" >= {since.ToUniversalTime()}
                """)
            .CountAsync(cancellationToken);
    }
}
