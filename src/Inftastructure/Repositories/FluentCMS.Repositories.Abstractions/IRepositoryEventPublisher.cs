namespace FluentCMS.Repositories.Abstractions;

public interface IRepositoryEventPublisher
{
    Task PublishCreated(RepositoryEntityCreatedEvent repositoryEvent, CancellationToken cancellationToken = default);
    Task PublishUpdated(RepositoryEntityUpdatedEvent repositoryEvent, CancellationToken cancellationToken = default);
    Task PublishRemoved(RepositoryEntityRemovedEvent repositoryEvent, CancellationToken cancellationToken = default);
}