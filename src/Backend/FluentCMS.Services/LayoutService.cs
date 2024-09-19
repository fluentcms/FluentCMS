namespace FluentCMS.Services;

public interface ILayoutService : IAutoRegisterService
{
    Task<IEnumerable<Layout>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default);
    Task<Layout> Create(Layout layout, CancellationToken cancellationToken = default);
    Task<Layout> Delete(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Layout>> DeleteBySiteId(Guid siteId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Layout>> GetAll(CancellationToken cancellationToken = default);
    Task<Layout> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<Layout> Update(Layout layout, CancellationToken cancellationToken);
}

public class LayoutService(ILayoutRepository layoutRepository, IMessagePublisher messagePublisher, IPermissionManager permissionManager) : ILayoutService
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
        if (!await permissionManager.HasAccess(layout.SiteId, SitePermissionAction.SiteAdmin, cancellationToken))
            throw new AppException(ExceptionCodes.PermissionDenied);

        var created = await layoutRepository.Create(layout, cancellationToken) ??
            throw new AppException(ExceptionCodes.LayoutUnableToCreate);

        await messagePublisher.Publish(new Message<Layout>(ActionNames.LayoutCreated, created), cancellationToken);

        return created;
    }

    public async Task<Layout> Update(Layout layout, CancellationToken cancellationToken)
    {
        var existing = await layoutRepository.GetById(layout.Id, cancellationToken) ??
            throw new AppException(ExceptionCodes.LayoutNotFound);

        if (!await permissionManager.HasAccess(existing.SiteId, SitePermissionAction.SiteAdmin, cancellationToken))
            throw new AppException(ExceptionCodes.PermissionDenied);

        var updated = await layoutRepository.Update(layout, cancellationToken) ??
            throw new AppException(ExceptionCodes.LayoutUnableToUpdate);

        await messagePublisher.Publish(new Message<Layout>(ActionNames.LayoutUpdated, updated), cancellationToken);

        return updated;
    }

    public async Task<Layout> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var existing = await layoutRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.LayoutNotFound);

        if (!await permissionManager.HasAccess(existing.SiteId, SitePermissionAction.SiteAdmin, cancellationToken))
            throw new AppException(ExceptionCodes.PermissionDenied);

        var deleted = await layoutRepository.Delete(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.LayoutUnableToDelete);

        await messagePublisher.Publish(new Message<Layout>(ActionNames.LayoutDeleted, deleted), cancellationToken);

        return deleted;
    }

    public Task<IEnumerable<Layout>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default)
    {
        return layoutRepository.GetAllForSite(siteId, cancellationToken);
    }

    public async Task<IEnumerable<Layout>> DeleteBySiteId(Guid siteId, CancellationToken cancellationToken = default)
    {
        if (!await permissionManager.HasAccess(siteId, SitePermissionAction.SiteAdmin, cancellationToken))
            throw new AppException(ExceptionCodes.PermissionDenied);

        var layouts = await layoutRepository.GetAllForSite(siteId, cancellationToken);

        var deleted = await layoutRepository.DeleteMany(layouts.Select(x => x.Id), cancellationToken);

        // creating tasks for the message publisher and await them all to finish
        var tasks = deleted?.Select(item => messagePublisher.Publish(new Message<Layout>(ActionNames.LayoutDeleted, item!), cancellationToken)).ToList() ?? [];

        await Task.WhenAll(tasks);

        return layouts;
    }
}
