using FluentCMS.Core.Agents;
using FluentCMS.Infrastructure.ChangeSets;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Agents;

// Counted from the audit log, which records every change set an agent opens with its credential (AUD-01), and from the tool
// call log (MCP-07).
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

    public Task<int> ToolCallsSinceAsync(Guid credentialId, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        var refused = nameof(ToolCallOutcome.Refused);

        return context.ToolCalls
            .FromSql($"""
                SELECT * FROM "ToolCalls"
                WHERE "AgentCredentialId" = {credentialId} AND "Timestamp" >= {since.ToUniversalTime()} AND "Outcome" <> {refused}
                """)
            .CountAsync(cancellationToken);
    }

    // A write to the credential's row: PostgreSQL holds the row lock and SQLite the write lock until the transaction ends.
    public async Task HoldAsync(Guid credentialId, CancellationToken cancellationToken = default)
    {
        await context.Database.ExecuteSqlAsync($"""UPDATE "AgentCredentials" SET "DisplayName" = "DisplayName" WHERE "Id" = {credentialId}""", cancellationToken);
    }

    public Task StartToolCallAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);

        context.ToolCalls.Add(ToolCallRow.From(call));
        return Task.CompletedTask;
    }
}
