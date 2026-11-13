using FluentCMS.Events;
using FluentCMS.Jobs;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.Search;

// The outbox handler of each entry event. An import and a seed raise the same events, so what they write is indexed too.
internal sealed class SearchEventHandler<TEvent>(SearchIndexer indexer) : IOutboxEventHandler<TEvent>
    where TEvent : EntryEvent
{
    public async ValueTask HandleAsync(TEvent message, EventDelivery delivery, CancellationToken cancellationToken) =>
        await indexer.IndexAsync(message.Type, message.EntryId, message.Locale, message.Source.SiteId, cancellationToken);
}

// The rows of a site go with it, through the outbox, so a failure is tried again. A site is deleted only when it has no
// content, so these are rows that an event never removed.
internal sealed class DeletedSiteSearch(SearchStore store, ILogger<DeletedSiteSearch> logger) : IOutboxEventHandler<SiteDeletedEvent>
{
    public async ValueTask HandleAsync(SiteDeletedEvent message, EventDelivery delivery, CancellationToken cancellationToken)
    {
        var removed = await store.RemoveSiteAsync(message.SiteId, cancellationToken);
        if (removed > 0)
        {
            SearchLog.SiteRemoved(logger, removed, message.SiteId);
        }
    }
}

// While the plugin was disabled in a site, its handlers heard nothing of the site, so enabling it again rebuilds the index
// of the site. A delivery that runs twice queues a second rebuild, which changes nothing.
internal sealed class EnabledSiteSearch(IJobScheduler scheduler, ILogger<EnabledSiteSearch> logger) : IOutboxEventHandler<PluginEnabledEvent>
{
    public async ValueTask HandleAsync(PluginEnabledEvent message, EventDelivery delivery, CancellationToken cancellationToken)
    {
        if (message.Plugin != SearchPlugin.Name)
        {
            return;
        }

        var job = await scheduler.EnqueueAsync<SearchRebuildJob, SearchRebuildPayload>(new SearchRebuildPayload(null), SearchRebuildJob.Options(message.SiteId), cancellationToken);
        SearchLog.RebuildQueued(logger, message.SiteId, job);
    }
}
