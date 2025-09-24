namespace FluentCMS.Plugins.AuditTrailManager.Handlers;

public class AuditTrailHandler(IAuditTrailService service, ILogger<AuditTrailHandler> logger) :
    IEventSubscriber<RepositoryEntityCreatedEvent>,
    IEventSubscriber<RepositoryEntityUpdatedEvent>,
    IEventSubscriber<RepositoryEntityRemovedEvent>

{
    private async Task HandleInternal(RepositoryEvent domainEvent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(domainEvent);
        ArgumentNullException.ThrowIfNull(domainEvent.Data);

        try
        {
            if (domainEvent.Data is IAuditableEntity)
                await service.Add(domainEvent.Data, domainEvent.EventType, cancellationToken);
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

    public Task Handle(RepositoryEntityRemovedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        return HandleInternal(domainEvent, cancellationToken);
    }
}
