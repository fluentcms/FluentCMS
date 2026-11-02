using FluentCMS.Content;

namespace FluentCMS.Events;

/// <summary>Where and when an event happened: the operation that raised it.</summary>
/// <param name="OccurredAt">When the operation began.</param>
/// <param name="SiteId">The site of the operation.</param>
/// <param name="ActorId">The id of the principal to audit, which is the actor a job impersonates or else the principal; empty for the system.</param>
/// <param name="ActorName">The name of that principal.</param>
/// <param name="Origin">Where the operation came from.</param>
/// <param name="CorrelationId">The correlation id of the operation.</param>
public sealed record EventSource(DateTimeOffset OccurredAt, Guid SiteId, Guid ActorId, string ActorName, Origin Origin, string CorrelationId)
{
    /// <summary>Describes an event raised by an operation.</summary>
    /// <param name="operation">The operation.</param>
    /// <returns>The source.</returns>
    public static EventSource From(OperationContext operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return new EventSource(operation.Timestamp, operation.Site.Value, operation.AuditPrincipal.Id, operation.AuditPrincipal.Name, operation.Origin, operation.CorrelationId);
    }

    /// <summary>Describes an event the system raised outside any request, such as a job's failure.</summary>
    /// <param name="occurredAt">When it happened.</param>
    /// <returns>The source.</returns>
    public static EventSource FromSystem(DateTimeOffset occurredAt) =>
        new(occurredAt, Guid.Empty, Principal.System.Id, Principal.System.Name, Origin.Job, "");
}
