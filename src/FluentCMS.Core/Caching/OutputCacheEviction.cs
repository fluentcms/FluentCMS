using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Events;
using FluentCMS.Schema;
using Microsoft.AspNetCore.OutputCaching;

namespace FluentCMS.Core.Caching;

// What changes the published state of an entry changes what the Delivery API answers, so it evicts the entry's tag and
// its type's from the output cache and raises CacheInvalidatedEvent with them, for a CDN to purge the same. It runs
// through the outbox, so a crash after the commit does not leave the cache stale; a retried delivery evicts again and
// raises the event again. Only the store of the instance that delivers is evicted, unless the store is shared. With the
// output cache off and no tag header nothing is recorded for it, as nothing was stored or sent. A change to a site's
// locales changes what fallback reads give, so it evicts the site, and so does a change to a role or its grants, which
// changes what the site's callers may read: the Public role decides the shared copy, and a key's role its own. A plugin
// disabled in a site evicts the site too, since a copy of its Delivery group's answer would be served without asking
// whether the plugin is enabled. A deleted site evicts its copies, which would otherwise be served until they expire.
internal sealed class OutputCacheEviction(IOutputCacheStore store, IEventBus events, SchemaModel schema)
    : IOutboxEventHandler<EntryPublishedEvent>,
      IOutboxEventHandler<EntryUnpublishedEvent>,
      IOutboxEventHandler<EntryTrashedEvent>,
      IOutboxEventHandler<EntryRestoredEvent>,
      IOutboxEventHandler<SiteUpdatedEvent>,
      IOutboxEventHandler<SiteDeletedEvent>,
      IOutboxEventHandler<RolePermissionsChangedEvent>,
      IOutboxEventHandler<PluginDisabledEvent>
{
    public static void Register(EventsBuilder events, Func<bool> active) =>
        events
            .AddOutboxHandler<EntryPublishedEvent, OutputCacheEviction>("cache.content.published", active)
            .AddOutboxHandler<EntryUnpublishedEvent, OutputCacheEviction>("cache.content.unpublished", active)
            .AddOutboxHandler<EntryTrashedEvent, OutputCacheEviction>("cache.content.trashed", active)
            .AddOutboxHandler<EntryRestoredEvent, OutputCacheEviction>("cache.content.restored", active)
            .AddOutboxHandler<SiteUpdatedEvent, OutputCacheEviction>("cache.site.updated", active)
            .AddOutboxHandler<SiteDeletedEvent, OutputCacheEviction>("cache.site.deleted", active)
            .AddOutboxHandler<RolePermissionsChangedEvent, OutputCacheEviction>("cache.role.changed", active)
            .AddOutboxHandler<PluginDisabledEvent, OutputCacheEviction>("cache.plugin.disabled", active);

    public ValueTask HandleAsync(SiteUpdatedEvent message, EventDelivery delivery, CancellationToken cancellationToken) =>
        EvictAsync([CacheTags.Site(message.SiteId)], cancellationToken);

    public ValueTask HandleAsync(SiteDeletedEvent message, EventDelivery delivery, CancellationToken cancellationToken) =>
        EvictAsync([CacheTags.Site(message.SiteId)], cancellationToken);

    public ValueTask HandleAsync(PluginDisabledEvent message, EventDelivery delivery, CancellationToken cancellationToken) =>
        EvictAsync([CacheTags.Site(message.SiteId)], cancellationToken);

    public ValueTask HandleAsync(RolePermissionsChangedEvent message, EventDelivery delivery, CancellationToken cancellationToken) =>
        message.Sites.Count == 0 ? ValueTask.CompletedTask : EvictAsync([.. message.Sites.Select(CacheTags.Site)], cancellationToken);

    public ValueTask HandleAsync(EntryPublishedEvent message, EventDelivery delivery, CancellationToken cancellationToken) => EvictAsync(message, cancellationToken);

    public ValueTask HandleAsync(EntryUnpublishedEvent message, EventDelivery delivery, CancellationToken cancellationToken) => EvictAsync(message, cancellationToken);

    public ValueTask HandleAsync(EntryTrashedEvent message, EventDelivery delivery, CancellationToken cancellationToken) => EvictAsync(message, cancellationToken);

    public ValueTask HandleAsync(EntryRestoredEvent message, EventDelivery delivery, CancellationToken cancellationToken) => EvictAsync(message, cancellationToken);

    private ValueTask EvictAsync(EntryEvent message, CancellationToken cancellationToken)
    {
        var global = schema.FindContentType(message.Type)?.Scope == ContentScope.Global;
        return EvictAsync([CacheTags.Entry(message.EntryId), CacheTags.Type(message.Type, global, message.Source.SiteId)], cancellationToken);
    }

    private async ValueTask EvictAsync(string[] tags, CancellationToken cancellationToken)
    {
        foreach (var tag in tags)
        {
            await store.EvictByTagAsync(tag, cancellationToken);
        }

        await events.PublishAsync(new CacheInvalidatedEvent(tags), cancellationToken);
    }
}
