using System.Globalization;
using FluentCMS.Activity;
using FluentCMS.Core.Events;
using FluentCMS.Events;

namespace FluentCMS.Core.ActivityLog;

// The events of the catalogue that exist, and what each is in the log. An entry of content is described by its type, id,
// locale and version, never by a field: a reader of the log may not be allowed to read the entry. A new event of the
// catalogue is added here, and the test that lists every event record fails until it is.
internal static class ActivityEvents
{
    public static void Register(EventsBuilder events)
    {
        Entry<EntryDraftSavedEvent>(events, "content.draft_saved");
        Entry<EntryPublishedEvent>(events, "content.published");
        Entry<EntryUnpublishedEvent>(events, "content.unpublished");
        Entry<EntryDraftDiscardedEvent>(events, "content.draft_discarded");
        Entry<EntryVersionRestoredEvent>(events, "content.version_restored");
        Entry<EntryTrashedEvent>(events, "content.trashed");
        Entry<EntryRestoredEvent>(events, "content.restored");
        Entry<EntryPurgedEvent>(events, "content.purged");

        events.AddActivity<EntryScheduledEvent>("content.scheduled", message => ForEntry("content.scheduled", message, new Dictionary<string, string?>
        {
            ["version"] = Number(message.Version),
            ["at"] = message.At.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["schedule"] = message.Action,
        }));

        events.AddActivity<EntryScheduleCancelledEvent>("content.schedule_cancelled", message => ForEntry("content.schedule_cancelled", message, new Dictionary<string, string?>
        {
            ["version"] = Number(message.Version),
            ["schedule"] = message.Action,
        }));

        events.AddActivity<SettingsChangedEvent>("settings.changed", message => new ActivityEntry("settings.changed")
        {
            TargetType = "settings",
            TargetId = message.Area,
            TargetLabel = message.Area,
        });

        events.AddActivity<SiteCreatedEvent>("site.created", message => Site("site.created", message.SiteId, message.Slug));
        events.AddActivity<SiteUpdatedEvent>("site.updated", message => Site("site.updated", message.SiteId, message.Slug));

        events.AddActivity<CacheInvalidatedEvent>("cache.invalidated", message => new ActivityEntry("cache.invalidated")
        {
            Details = new Dictionary<string, string?>
            {
                ["tags"] = string.Join(',', message.Tags),
                ["count"] = Number(message.Tags.Count),
            },
        });

        events.AddActivity<JobFailedEvent>("job.failed", message => new ActivityEntry("job.failed")
        {
            TargetType = "job",
            TargetId = message.JobId.ToString("D"),
            TargetLabel = message.Type,
            Details = new Dictionary<string, string?>
            {
                ["queue"] = message.Queue,
                ["attempt"] = Number(message.Attempt),
                ["maxAttempts"] = Number(message.MaxAttempts),
                ["error"] = message.Error,
            },
        });

        events.AddActivity<JobDeadLetteredEvent>("job.dead_lettered", message => new ActivityEntry("job.dead_lettered")
        {
            TargetType = "job",
            TargetId = message.JobId.ToString("D"),
            TargetLabel = message.Type,
            Details = new Dictionary<string, string?>
            {
                ["queue"] = message.Queue,
                ["attempts"] = Number(message.Attempts),
                ["error"] = message.Error,
            },
        });
    }

    private static void Entry<TEvent>(EventsBuilder events, string action)
        where TEvent : EntryEvent =>
        events.AddActivity<TEvent>(action, message => ForEntry(action, message, new Dictionary<string, string?> { ["version"] = Number(message.Version) }));

    private static ActivityEntry ForEntry(string action, EntryEvent message, Dictionary<string, string?> details) =>
        new(action)
        {
            TargetType = message.Type,
            TargetId = message.EntryId.ToString("D"),
            Locale = message.Locale,
            Details = details,
        };

    private static ActivityEntry Site(string action, Guid siteId, string slug) =>
        new(action)
        {
            TargetType = "site",
            TargetId = siteId.ToString("D"),
            TargetLabel = slug,
        };

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
