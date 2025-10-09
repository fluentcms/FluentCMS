namespace FluentCMS.Infrastructure.Repositories.EntityFramework.Interceptors;

/// <summary>
/// Intercepts Entity Framework save operations to publish domain events
/// Should be registered after the AuditableEntitySaveChangesInterceptor.
/// </summary>
public class DomainEventSaveChangesInterceptor(IEventPublisher eventPublisher) : ISaveChangesInterceptor
{
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
            switch (entry.State)
            {
                case EntityState.Deleted:
                    var deleteEvent = new RepositoryEntityDeletedEvent(entry.Entity);
                    eventPublisher.Publish(deleteEvent).GetAwaiter().GetResult();
                    break;
                case EntityState.Modified:
                    var updateEvent = new RepositoryEntityUpdatedEvent(entry.Entity);
                    eventPublisher.Publish(updateEvent).GetAwaiter().GetResult();
                    break;
                case EntityState.Added:
                    var createEvent = new RepositoryEntityCreatedEvent(entry.Entity);
                    eventPublisher.Publish(createEvent).GetAwaiter().GetResult();
                    break;
                default:
                    break;
            }
        }
    }

    private async Task PublishDomainEvents(DbContext context, CancellationToken cancellationToken)
    {
        foreach (var entry in context.ChangeTracker.Entries())
        {
            switch (entry.State)
            {
                case EntityState.Deleted:
                    var deleteEvent = new RepositoryEntityDeletedEvent(entry.Entity);
                    await eventPublisher.Publish(deleteEvent, cancellationToken);
                    break;
                case EntityState.Modified:
                    var updateEvent = new RepositoryEntityUpdatedEvent(entry.Entity);
                    await eventPublisher.Publish(updateEvent, cancellationToken);
                    break;
                case EntityState.Added:
                    var createEvent = new RepositoryEntityCreatedEvent(entry.Entity);
                    await eventPublisher.Publish(createEvent, cancellationToken);
                    break;
                default:
                    break;
            }
        }
    }
}

