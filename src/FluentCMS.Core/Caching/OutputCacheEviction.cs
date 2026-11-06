using FluentCMS.Core.Events;
using FluentCMS.Events;
using FluentCMS.Schema;
using Microsoft.AspNetCore.OutputCaching;

namespace FluentCMS.Core.Caching;

// What changes the published state of an entry changes what the Delivery API answers, so it evicts the entry's tag and
// its type's from the output cache and raises CacheInvalidatedEvent with them, for a CDN to purge the same. It runs
// through the outbox, so a crash after the commit does not leave the cache stale; a retried delivery evicts again and
// raises the event again. Only the store of the instance that delivers is evicted. With the output cache off nothing is
// recorded for it, as nothing was stored. A change to a site's locales changes what fallback reads give, so it evicts the site.
internal sealed class OutputCacheEviction(IOutputCacheStore store, IEventBus events, SchemaModel schema)
    : IOutboxEventHandler<EntryPublishedEvent>,
      IOutboxEventHandler<EntryUnpublishedEvent>,
      IOutboxEventHandler<EntryTrashedEvent>,
      IOutboxEventHandler<EntryRestoredEvent>,
      IOutboxEventHandler<SiteUpdatedEvent>
{
    public static void Register(EventsBuilder events, Func<bool> active) =>
        events
            .AddOutboxHandler<EntryPublishedEvent, OutputCacheEviction>("cache.content.published", active)
            .AddOutboxHandler<EntryUnpublishedEvent, OutputCacheEviction>("cache.content.unpublished", active)
            .AddOutboxHandler<EntryTrashedEvent, OutputCacheEviction>("cache.content.trashed", active)
            .AddOutboxHandler<EntryRestoredEvent, OutputCacheEviction>("cache.content.restored", active)
            .AddOutboxHandler<SiteUpdatedEvent, OutputCacheEviction>("cache.site.updated", active);

    public ValueTask HandleAsync(SiteUpdatedEvent message, EventDelivery delivery, CancellationToken cancellationToken) =>
        EvictAsync([CacheTags.Site(message.SiteId)], cancellationToken);

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
