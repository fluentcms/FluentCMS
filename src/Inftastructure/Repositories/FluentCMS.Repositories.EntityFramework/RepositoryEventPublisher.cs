namespace FluentCMS.Repositories.EntityFramework;

public class RepositoryEventPublisher(IEventPublisher eventPublisher) : IRepositoryEventPublisher
{
    public async Task PublishCreated(RepositoryEntityCreatedEvent repositoryEvent, CancellationToken cancellationToken = default)
    {
        await eventPublisher.Publish(repositoryEvent, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishRemoved(RepositoryEntityRemovedEvent repositoryEvent, CancellationToken cancellationToken = default)
    {
        await eventPublisher.Publish(repositoryEvent, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishUpdated(RepositoryEntityUpdatedEvent repositoryEvent, CancellationToken cancellationToken = default)
    {
        await eventPublisher.Publish(repositoryEvent, cancellationToken).ConfigureAwait(false);
    }
}