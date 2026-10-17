namespace FluentCMS.Core.Audit;

public interface IAuditLog
{
    // Appends inside the current unit of work, stamping the current initiator and time itself, so a caller cannot record someone else as the actor.
    // Throws when no unit of work is active or no initiator is known: a change without provenance cannot exist (AUD-N1).
    Task<AuditEvent> AppendAsync(NewAuditEvent entry, CancellationToken cancellationToken = default);
}
