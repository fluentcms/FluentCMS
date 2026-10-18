using FluentCMS.Core.Audit;
using FluentCMS.Core.Identity;
using FluentCMS.Infrastructure.Persistence;

namespace FluentCMS.Infrastructure.Audit;

internal sealed class AuditLog(FluentCmsDbContext context, ICurrentInitiator currentInitiator, TimeProvider clock) : IAuditLog
{
    public Task<AuditEvent> AppendAsync(NewAuditEvent entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Audit events are appended inside a unit of work, in the same transaction as the change they describe (AUD-N1).");
        }

        var initiator = currentInitiator.Current
            ?? throw new InvalidOperationException("No initiator is known for this operation; the audit log records who acted (AUD-01).");

        var timestamp = clock.GetUtcNow();
        var auditEvent = new AuditEvent
        {
            Id = Guid.CreateVersion7(timestamp),
            Timestamp = timestamp,
            Initiator = initiator,
            Operation = entry.Operation,
            SubjectType = entry.SubjectType,
            SubjectId = entry.SubjectId,
            Model = entry.Model,
            PromptHash = entry.PromptHash,
            Sources = entry.Sources,
            ChangeSetId = entry.ChangeSetId,
            PreviousVersionId = entry.PreviousVersionId,
        };

        // The row reaches the database with the next save inside the transaction, so it commits with the change or not at all.
        context.AuditEvents.Add(AuditEventRow.From(auditEvent));

        return Task.FromResult(auditEvent);
    }
}
