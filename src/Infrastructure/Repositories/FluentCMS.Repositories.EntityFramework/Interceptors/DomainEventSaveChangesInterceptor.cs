using FluentCMS.Providers.EventBus.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FluentCMS.Repositories.EntityFramework.Interceptors;

/// <summary>
/// Intercepts Entity Framework save operations to publish domain events
/// Should be registered after the AuditableEntitySaveChangesInterceptor.
/// </summary>
public class DomainEventSaveChangesInterceptor : ISaveChangesInterceptor
{
    private readonly IEventPublisher _eventPublisher;

    public DomainEventSaveChangesInterceptor(IEventPublisher eventPublisher)
    {
        ArgumentNullException.ThrowIfNull(eventPublisher);
        _eventPublisher = eventPublisher;
    }

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context == null)
            return result;

        // Publish domain events synchronously after audit fields are updated but before saving
        PublishDomainEventsSync(eventData.Context);
        return result;
    }

    public async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context == null)
            return result;

        // Publish domain events after audit fields are updated but before saving
        await PublishDomainEvents(eventData.Context, cancellationToken);
        return result;
    }

    private void PublishDomainEventsSync(DbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries())
        {
            // Use reflection to create and publish the appropriate event type
            IEvent? domainEvent = null;
            switch (entry.State)
            {
                case EntityState.Deleted:
                    domainEvent = CreateEntityEvent(typeof(EntityDeletedEvent<>), entry.Entity);
                    break;
                case EntityState.Modified:
                    domainEvent = CreateEntityEvent(typeof(EntityUpdatedEvent<>), entry.Entity);
                    break;
                case EntityState.Added:
                    domainEvent = CreateEntityEvent(typeof(EntityCreatedEvent<>), entry.Entity);
                    break;
                default:
                    break;
            }

            if (domainEvent != null)
                _eventPublisher.Publish(domainEvent).GetAwaiter().GetResult();
        }
    }

    private async Task PublishDomainEvents(DbContext context, CancellationToken cancellationToken)
    {
        foreach (var entry in context.ChangeTracker.Entries())
        {
            // Use reflection to create and publish the appropriate event type
            IEvent? domainEvent = null;
            switch (entry.State)
            {
                case EntityState.Deleted:
                    domainEvent = CreateEntityEvent(typeof(EntityDeletedEvent<>), entry.Entity);
                    break;
                case EntityState.Modified:
                    domainEvent = CreateEntityEvent(typeof(EntityUpdatedEvent<>), entry.Entity);
                    break;
                case EntityState.Added:
                    domainEvent = CreateEntityEvent(typeof(EntityCreatedEvent<>), entry.Entity);
                    break;
                default:
                    break;
            }

            if (domainEvent != null)
                await _eventPublisher.Publish(domainEvent, cancellationToken);
        }
    }

    private static EventBase CreateEntityEvent(Type eventType, object entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        // Get the runtime type of the entity
        var entityType = entity.GetType();

        // Create the generic EntityDeletedEvent<TEntity> type
        var eventTypeGeneric = eventType.MakeGenericType(entityType);

        // Create an instance: new EntityDeletedEvent<entityType>(entity)
        var domainEvent = (EventBase)Activator.CreateInstance(eventTypeGeneric, entity)!;

        return domainEvent;
    }
}

