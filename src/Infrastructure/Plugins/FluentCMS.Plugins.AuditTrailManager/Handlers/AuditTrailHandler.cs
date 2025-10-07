using FluentCMS.EventBus.Abstractions;

namespace FluentCMS.Plugins.AuditTrailManager.Handlers;

/// <summary>
/// Handles audit trail events for repository entity changes.
/// It listens to entity created, updated, and deleted events and records them using the IAuditTrailService.
/// This ensures that all changes to auditable entities are tracked for auditing purposes.
/// It is registered as a transient service to ensure a new instance is created for each event handling.
/// </summary>
public class AuditTrailHandler(IAuditTrailService auditTrailService, ILogger<AuditTrailHandler> logger) :
    IEventSubscriber<RepositoryEntityCreatedEvent>,
    IEventSubscriber<RepositoryEntityUpdatedEvent>,
    IEventSubscriber<RepositoryEntityDeletedEvent>

{
    private async Task HandleInternal(RepositoryEntityEvent domainEvent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(domainEvent);
        ArgumentNullException.ThrowIfNull(domainEvent.Entity);

        try
        {
            if (domainEvent.Entity is IAuditableEntity)
                await auditTrailService.Add(domainEvent.Entity, domainEvent.EventType, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to record AuditTrail for {domainEvent}", domainEvent.EventType);

            // TODO: should we throw?
            throw;
        }
    }

    public Task Handle(RepositoryEntityCreatedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        return HandleInternal(domainEvent, cancellationToken);
    }

    public Task Handle(RepositoryEntityUpdatedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        return HandleInternal(domainEvent, cancellationToken);
    }

    public Task Handle(RepositoryEntityDeletedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        return HandleInternal(domainEvent, cancellationToken);
    }
}
