namespace FluentCMS.Api.Core.Services;

public interface ILayoutService
{
    Task<Layout> Add(Layout layout, CancellationToken cancellationToken = default);
    Task<Layout> Remove(Guid id, CancellationToken cancellationToken = default);
    Task<Layout> Update(Layout layout, CancellationToken cancellationToken);
    Task<IEnumerable<Layout>> RemoveBySiteId(Guid siteId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Layout>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default);
    Task<Layout> GetById(Guid id, CancellationToken cancellationToken = default);
}

internal class LayoutService(ILayoutRepository layoutRepository, ISiteRepository siteRepository, IEventPublisher eventPublisher) : ILayoutService
{
    public async Task<Layout> Add(Layout layout, CancellationToken cancellationToken = default)
    {
        await layoutRepository.Add(layout, cancellationToken);

        await eventPublisher.Publish(new LayoutAddedEvent(layout), cancellationToken);

        return layout;
    }

    public async Task<Layout> Remove(Guid id, CancellationToken cancellationToken = default)
    {
        var layout = await layoutRepository.GetById(id, cancellationToken);

        // check if the layout is one of site's default layout, if so, throw an exception
        var site = await siteRepository.GetById(layout.SiteId, cancellationToken);

        if (site.LayoutId == id || site.EditLayoutId == id || site.DetailLayoutId == id)
            throw new EnhancedException(ExceptionCodes.LayoutUnableToDeleteDefaultLayout);

        await layoutRepository.Remove(layout, cancellationToken);

        await eventPublisher.Publish(new LayoutRemovedEvent(layout), cancellationToken);

        return layout;
    }

    public async Task<Layout> Update(Layout layout, CancellationToken cancellationToken)
    {
        await layoutRepository.Update(layout, cancellationToken);

        await eventPublisher.Publish(new LayoutUpdatedEvent(layout), cancellationToken);

        return layout;
    }

    public async Task<IEnumerable<Layout>> RemoveBySiteId(Guid siteId, CancellationToken cancellationToken = default)
    {
        var layouts = await layoutRepository.GetAllForSite(siteId, cancellationToken);

        await layoutRepository.RemoveRange(layouts, cancellationToken);

        await eventPublisher.Publish(new LayoutsRemovedBySiteEvent(layouts), cancellationToken);

        return layouts;
    }

    public async Task<Layout> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return await layoutRepository.GetById(id, cancellationToken);
    }

    public async Task<IEnumerable<Layout>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default)
    {
        return await layoutRepository.GetAllForSite(siteId, cancellationToken);
    }
}
