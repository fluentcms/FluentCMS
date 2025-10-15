namespace FluentCMS.Api.Core.Services;

public interface ILayoutService
{
    Task<Layout> Add(Layout layout, CancellationToken cancellationToken = default);
    Task<Layout> Remove(Guid id, CancellationToken cancellationToken = default);
    Task<Layout> Update(Layout layout, CancellationToken cancellationToken);
    Task<IEnumerable<Layout>> RemoveBySiteId(Guid siteId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Layout>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Layout>> GetAll(CancellationToken cancellationToken = default);
    Task<Layout> GetById(Guid id, CancellationToken cancellationToken = default);
}

public class LayoutService(ILayoutRepository layoutRepository, ISiteRepository siteRepository, IEventPublisher messagePublisher, IPermissionManager permissionManager) : ILayoutService
{
    public async Task<Layout> Add(Layout layout, CancellationToken cancellationToken = default)
    {
        if (!await permissionManager.HasAccess(layout.SiteId, SitePermissionAction.SiteAdmin, cancellationToken))
            throw new EnhancedException(ExceptionCodes.PermissionDenied);

        var created = await layoutRepository.Add(layout, cancellationToken) ??
            throw new EnhancedException(ExceptionCodes.LayoutUnableToCreate);

        await messagePublisher.Publish(new LayoutAddedEvent(created), cancellationToken);

        return created;
    }

    public async Task<Layout> Remove(Guid id, CancellationToken cancellationToken = default)
    {
        var existing = await layoutRepository.GetById(id, cancellationToken) ??
            throw new EnhancedException(ExceptionCodes.LayoutNotFound);

        var siteId = existing.SiteId;

        if (!await permissionManager.HasAccess(siteId, SitePermissionAction.SiteAdmin, cancellationToken))
            throw new EnhancedException(ExceptionCodes.PermissionDenied);

        // check if the layout is one of site's default layout, if so, throw an exception
        var site = await siteRepository.GetById(siteId, cancellationToken) ??
            throw new EnhancedException(ExceptionCodes.SiteNotFound);

        if (site.LayoutId == id || site.EditLayoutId == id || site.DetailLayoutId == id)
            throw new EnhancedException(ExceptionCodes.LayoutUnableToDeleteDefaultLayout);

        var deleted = await layoutRepository.Remove(id, cancellationToken) ??
            throw new EnhancedException(ExceptionCodes.LayoutUnableToDelete);

        await messagePublisher.Publish(new LayoutRemovedEvent(deleted), cancellationToken);

        return deleted;
    }

    public async Task<Layout> Update(Layout layout, CancellationToken cancellationToken)
    {
        var existing = await layoutRepository.GetById(layout.Id, cancellationToken) ??
            throw new EnhancedException(ExceptionCodes.LayoutNotFound);

        if (!await permissionManager.HasAccess(existing.SiteId, SitePermissionAction.SiteAdmin, cancellationToken))
            throw new EnhancedException(ExceptionCodes.PermissionDenied);

        var updated = await layoutRepository.Update(layout, cancellationToken) ??
            throw new EnhancedException(ExceptionCodes.LayoutUnableToUpdate);

        await messagePublisher.Publish(new LayoutUpdatedEvent(updated), cancellationToken);

        return updated;
    }

    public async Task<IEnumerable<Layout>> RemoveBySiteId(Guid siteId, CancellationToken cancellationToken = default)
    {
        if (!await permissionManager.HasAccess(siteId, SitePermissionAction.SiteAdmin, cancellationToken))
            throw new EnhancedException(ExceptionCodes.PermissionDenied);

        var layouts = await layoutRepository.GetAllForSite(siteId, cancellationToken);

        var deleted = await layoutRepository.RemoveRange(layouts, cancellationToken);

        await messagePublisher.Publish(new LayoutsRemovedBySiteEvent(deleted), cancellationToken);

        return layouts;
    }

    public async Task<Layout> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var layout = await layoutRepository.GetById(id, cancellationToken);

        return layout ?? throw new EnhancedException(ExceptionCodes.LayoutNotFound);
    }

    public async Task<IEnumerable<Layout>> GetAll(CancellationToken cancellationToken = default)
    {
        return await layoutRepository.GetAll(cancellationToken);
    }

    public Task<IEnumerable<Layout>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default)
    {
        return layoutRepository.GetAllForSite(siteId, cancellationToken);
    }
}
