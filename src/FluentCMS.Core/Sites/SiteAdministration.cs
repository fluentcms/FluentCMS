using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Events;
using FluentCMS.Sites;

namespace FluentCMS.Core.Sites;

// The SDK's read, create and delete of a site, over SiteManager, for a plugin such as the multi-site plugin. Each needs
// sites.manage for the instance, since a new site is in no site yet and a deleted one takes its roles with it. The events
// are raised after the commit, and their activity rows, site.created and site.deleted, are the events' (ActivityEvents).
internal sealed class SiteAdministration(SiteManager manager, ManagementAuthority authority, IEventBus bus, IOperationContextAccessor accessor) : ISiteAdministration
{
    public async Task<SiteInfo?> FindAsync(SiteId site, CancellationToken cancellationToken = default)
    {
        await authority.RequireAsync(CorePermissions.SitesManage, null, cancellationToken);
        return await manager.FindInfoAsync(site.Value, cancellationToken);
    }

    public async Task<SiteInfo> CreateAsync(string name, string slug, string defaultLocale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(slug);
        ArgumentNullException.ThrowIfNull(defaultLocale);

        await authority.RequireAsync(CorePermissions.SitesManage, null, cancellationToken);
        var site = await manager.CreateAsync(name, slug, defaultLocale, cancellationToken);

        // What happens to a site is that site's, as for SiteUpdatedEvent.
        await bus.PublishAsync(new SiteCreatedEvent(site.Id, site.Slug) { Source = Source() with { SiteId = site.Id } }, CancellationToken.None);
        return SiteManager.Info(site);
    }

    public async Task DeleteAsync(SiteId site, CancellationToken cancellationToken = default)
    {
        await authority.RequireAsync(CorePermissions.SitesManage, null, cancellationToken);
        var deleted = await manager.DeleteAsync(site.Value, cancellationToken);

        // The site is gone, so the event and its activity row are the instance's: no one could read the log of that site.
        await bus.PublishAsync(new SiteDeletedEvent(deleted.Id, deleted.Slug) { Source = Source() with { SiteId = Guid.Empty } }, CancellationToken.None);
    }

    // The permission check has refused a call outside an operation.
    private EventSource Source() => EventSource.From(accessor.Current!);
}
