namespace FluentCMS.Repositories.EntityFramework.Interceptors;

public class RepositoryEventBusPublisherInterceptor(IRepositoryEventPublisher publisher, ILogger<RepositoryEventBusPublisherInterceptor> logger) : BaseSaveChangesInterceptor
{
    public override async Task AfterSaveChanges(DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        foreach (var entry in eventData.Context!.ChangeTracker.Entries<IEntity>())
        {
            var entity = entry.Entity;
            switch (entry.State)
            {
                case EntityState.Added:
                    await publisher.PublishCreated(RepositoryEntityCreatedEvent.Create(entity), cancellationToken).ConfigureAwait(false);
                    logger.LogInformation($"Published created event for entity of type {entity.GetType().Name} with ID {entity.Id}.");
                    break;
                case EntityState.Modified:
                    await publisher.PublishUpdated(RepositoryEntityUpdatedEvent.Create(entity), cancellationToken).ConfigureAwait(false);
                    logger.LogInformation($"Published updated event for entity of type {entity.GetType().Name} with ID {entity.Id}.");
                    break;
                case EntityState.Deleted:
                    await publisher.PublishRemoved(RepositoryEntityRemovedEvent.Create(entity), cancellationToken).ConfigureAwait(false);
                    logger.LogInformation($"Published removed event for entity of type {entity.GetType().Name} with ID {entity.Id}.");
                    break;
            }
        }
    }
}