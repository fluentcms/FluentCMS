namespace FluentCMS.Api.Plugins.CmsCoreManagement.Services;

public interface ILayoutService
{
    Task<IEnumerable<Layout>> GetAll(CancellationToken cancellationToken = default);
    Task<Layout> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<Layout> Add(Layout layout, CancellationToken cancellationToken = default);
    Task<Layout> Update(Layout layout, CancellationToken cancellationToken = default);
    Task<Layout> Remove(Guid id, CancellationToken cancellationToken = default);
}

internal class LayoutService(ILayoutRepository layoutRepository, IEventPublisher eventPublisher) : ILayoutService
{
    public async Task<IEnumerable<Layout>> GetAll(CancellationToken cancellationToken = default)
    {
        return await layoutRepository.GetAll(cancellationToken);
    }

    public async Task<Layout> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return await layoutRepository.GetById(id, cancellationToken);
    }

    public async Task<Layout> Add(Layout layout, CancellationToken cancellationToken = default)
    {

        await layoutRepository.Add(layout, cancellationToken);

        await eventPublisher.Publish(new LayoutAddedEvent(layout), cancellationToken);

        return layout;
    }

    public async Task<Layout> Update(Layout layout, CancellationToken cancellationToken = default)
    {
        await layoutRepository.Update(layout, cancellationToken);

        await eventPublisher.Publish(new LayoutUpdatedEvent(layout), cancellationToken);

        return layout;
    }

    public async Task<Layout> Remove(Guid id, CancellationToken cancellationToken = default)
    {
        var deletedLayout = await layoutRepository.Remove(id, cancellationToken);

        await eventPublisher.Publish(new LayoutRemovedEvent(deletedLayout), cancellationToken);

        return deletedLayout;
    }
}
