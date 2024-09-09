using FluentCMS.Providers.MessageBusProviders;

namespace FluentCMS.Services;

public interface ILayoutService : IAutoRegisterService
{
    Task<IEnumerable<Layout>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default);
    Task<Layout> Create(Layout layout, CancellationToken cancellationToken = default);
    Task<Layout> Delete(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Layout>> GetAll(CancellationToken cancellationToken = default);
    Task<Layout> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<Layout> Update(Layout layout, CancellationToken cancellationToken);
}

public class LayoutService(ILayoutRepository layoutRepository, IMessagePublisher messagePublisher) : ILayoutService
{
    public async Task<Layout> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var layout = await layoutRepository.GetById(id, cancellationToken);

        return layout ?? throw new AppException(ExceptionCodes.LayoutNotFound);
    }

    public async Task<IEnumerable<Layout>> GetAll(CancellationToken cancellationToken = default)
    {
        return await layoutRepository.GetAll(cancellationToken);
    }

    public async Task<Layout> Create(Layout layout, CancellationToken cancellationToken = default)
    {
        var created = await layoutRepository.Create(layout, cancellationToken) ??
            throw new AppException(ExceptionCodes.LayoutUnableToCreate);

        await messagePublisher.Publish(new Message<Layout>(ActionNames.LayoutCreated, created), cancellationToken);

        return created;
    }

    public async Task<Layout> Update(Layout layout, CancellationToken cancellationToken)
    {
        var updated = await layoutRepository.Update(layout, cancellationToken) ??
            throw new AppException(ExceptionCodes.LayoutUnableToUpdate);

        await messagePublisher.Publish(new Message<Layout>(ActionNames.LayoutUpdated, updated), cancellationToken);

        return updated;
    }

    public async Task<Layout> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var deleted = await layoutRepository.Delete(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.LayoutUnableToDelete);

        await messagePublisher.Publish(new Message<Layout>(ActionNames.LayoutDeleted, deleted), cancellationToken);

        return deleted;
    }

    public Task<IEnumerable<Layout>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default)
    {
        return layoutRepository.GetAllForSite(siteId, cancellationToken);
    }
}
